using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Vortex.Core.Database
{
    public class DatabaseService
    {
        private readonly string _dbPath;

        public DatabaseService(string? customDbPath = null)
        {
            if (!string.IsNullOrEmpty(customDbPath))
            {
                _dbPath = customDbPath;
                string? dir = Path.GetDirectoryName(_dbPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
            else
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string vortexDir = Path.Combine(appData, "Vortex");
                if (!Directory.Exists(vortexDir))
                {
                    Directory.CreateDirectory(vortexDir);
                }
                _dbPath = Path.Combine(vortexDir, "vortex_data.db");
            }
            InitializeDatabase();
        }

        private SqliteConnection GetConnection()
        {
            var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            return connection;
        }

        private void InitializeDatabase()
        {
            using var conn = GetConnection();
            using (var pragmaCmd = conn.CreateCommand())
            {
                pragmaCmd.CommandText = @"
                    PRAGMA journal_mode = WAL;
                    PRAGMA synchronous = NORMAL;
                    PRAGMA busy_timeout = 5000;
                ";
                pragmaCmd.ExecuteNonQuery();
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ProcessKnowledgeBase (
                    ProcessName TEXT PRIMARY KEY,
                    DisplayName TEXT,
                    Publisher TEXT,
                    Description TEXT,
                    SafetyLevel INTEGER,
                    SafeToEnd INTEGER,
                    Recommendation TEXT
                );

                CREATE TABLE IF NOT EXISTS FolderFingerprints (
                    Pattern TEXT PRIMARY KEY,
                    OwningApplication TEXT,
                    Publisher TEXT,
                    Category TEXT,
                    Description TEXT,
                    Safety INTEGER,
                    Recommendation TEXT
                );

                CREATE TABLE IF NOT EXISTS AuditLog (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp TEXT,
                    ActionType TEXT,
                    Target TEXT,
                    BytesReclaimed INTEGER,
                    Details TEXT
                );

                CREATE TABLE IF NOT EXISTS QuarantineRegistry (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    OriginalPath TEXT,
                    QuarantinePath TEXT,
                    FileSize INTEGER,
                    DeletedDate TEXT,
                    IsRestored INTEGER
                );

                CREATE TABLE IF NOT EXISTS Settings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT
                );

                CREATE TABLE IF NOT EXISTS AppAccessRules (
                    ProcessName TEXT PRIMARY KEY,
                    DisplayName TEXT,
                    ExecutablePath TEXT,
                    AccessStatus TEXT,
                    MuteNotifications INTEGER,
                    IsBuiltIn INTEGER,
                    Category TEXT,
                    DateAdded TEXT
                );

                CREATE TABLE IF NOT EXISTS VaultFolders (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FolderPath TEXT UNIQUE,
                    FolderName TEXT,
                    ProtectionMode TEXT,
                    IsLocked INTEGER,
                    IsStealth INTEGER,
                    TotalSizeBytes INTEGER,
                    FileCount INTEGER,
                    EncryptedPayloadPath TEXT,
                    CreatedAt TEXT,
                    LastUnlockedAt TEXT
                );

                CREATE TABLE IF NOT EXISTS VaultSettings (
                    Id INTEGER PRIMARY KEY CHECK (Id = 1),
                    IsSetup INTEGER,
                    PasswordHash TEXT,
                    PasswordSalt TEXT,
                    RecoveryKeyHash TEXT,
                    WrappedMasterKeyPassword TEXT,
                    WrappedMasterKeyRecovery TEXT,
                    AutoLockOnScreenLock INTEGER,
                    AutoLockIdleMinutes INTEGER,
                    UsbWriteBlockEnabled INTEGER,
                    UsbAllowedUntil TEXT,
                    ShellContextMenuEnabled INTEGER
                );
            ";
            cmd.ExecuteNonQuery();

            // Migration: Ensure wrapped master key columns exist in legacy databases
            try
            {
                using var migCmd = conn.CreateCommand();
                migCmd.CommandText = "ALTER TABLE VaultSettings ADD COLUMN WrappedMasterKeyPassword TEXT";
                migCmd.ExecuteNonQuery();
            }
            catch { }

            try
            {
                using var migCmd = conn.CreateCommand();
                migCmd.CommandText = "ALTER TABLE VaultSettings ADD COLUMN WrappedMasterKeyRecovery TEXT";
                migCmd.ExecuteNonQuery();
            }
            catch { }

            SeedProcessKnowledge(conn);
            SeedFolderFingerprints(conn);

            // Ensure no unwanted pre-seeded rules exist
            using (var cleanCmd = conn.CreateCommand())
            {
                cleanCmd.CommandText = "DELETE FROM AppAccessRules WHERE IsBuiltIn = 1";
                cleanCmd.ExecuteNonQuery();
            }
        }

        private void SeedProcessKnowledge(SqliteConnection conn)
        {
            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM ProcessKnowledgeBase";
            long count = (long)(checkCmd.ExecuteScalar() ?? 0L);
            if (count > 0) return;

            var seeds = new List<ProcessRule>
            {
                // Critical System Processes (Strictly Protected)
                new() { ProcessName = "system", DisplayName = "Windows NT Kernel", Publisher = "Microsoft Corporation", Description = "Core Windows operating system kernel.", SafetyLevel = ProcessSafetyLevel.SystemCritical, SafeToEnd = false, Recommendation = "Never terminate. Essential for OS operation." },
                new() { ProcessName = "smss", DisplayName = "Session Manager Subsystem", Publisher = "Microsoft Corporation", Description = "Initializes user sessions and page files.", SafetyLevel = ProcessSafetyLevel.SystemCritical, SafeToEnd = false, Recommendation = "Never terminate. Critical Windows process." },
                new() { ProcessName = "csrss", DisplayName = "Client Server Runtime", Publisher = "Microsoft Corporation", Description = "Manages Win32 console and GUI threads.", SafetyLevel = ProcessSafetyLevel.SystemCritical, SafeToEnd = false, Recommendation = "Never terminate. Windows will Blue Screen." },
                new() { ProcessName = "wininit", DisplayName = "Windows Start-Up Application", Publisher = "Microsoft Corporation", Description = "Launches Windows system services and credentials.", SafetyLevel = ProcessSafetyLevel.SystemCritical, SafeToEnd = false, Recommendation = "Never terminate. Protected core process." },
                new() { ProcessName = "services", DisplayName = "Service Control Manager", Publisher = "Microsoft Corporation", Description = "Controls Windows background service daemons.", SafetyLevel = ProcessSafetyLevel.SystemCritical, SafeToEnd = false, Recommendation = "Never terminate. Essential service controller." },
                new() { ProcessName = "lsass", DisplayName = "Local Security Authority", Publisher = "Microsoft Corporation", Description = "Enforces Windows security policies and user logins.", SafetyLevel = ProcessSafetyLevel.SystemCritical, SafeToEnd = false, Recommendation = "Never terminate. Critical authentication process." },
                new() { ProcessName = "dwm", DisplayName = "Desktop Window Manager", Publisher = "Microsoft Corporation", Description = "Renders visual desktop effects and window compositing.", SafetyLevel = ProcessSafetyLevel.SystemCritical, SafeToEnd = false, Recommendation = "System GUI manager. Terminates only on screen reset." },
                new() { ProcessName = "explorer", DisplayName = "Windows File Explorer & Shell", Publisher = "Microsoft Corporation", Description = "Taskbar, Start Menu, and desktop icon manager.", SafetyLevel = ProcessSafetyLevel.SystemCritical, SafeToEnd = false, Recommendation = "Restart only if taskbar freezes; do not kill." },

                // Bloatware & Non-Essential Helpers (Safe to Terminate)
                new() { ProcessName = "Creative Cloud UI Helper", DisplayName = "Adobe Creative Cloud UI Helper", Publisher = "Adobe Systems", Description = "Background renderer for Adobe Creative Cloud menus and font syncing.", SafetyLevel = ProcessSafetyLevel.SafeToTerminate, SafeToEnd = true, Recommendation = "Safe to end. Frees 100MB+ RAM when Adobe apps are not in use." },
                new() { ProcessName = "AdobeIPCBroker", DisplayName = "Adobe IPC Broker", Publisher = "Adobe Systems", Description = "Inter-process coordinator for Adobe software updates.", SafetyLevel = ProcessSafetyLevel.SafeToTerminate, SafeToEnd = true, Recommendation = "Safe to terminate. Restarts automatically when Adobe launches." },
                new() { ProcessName = "AdobeDesktopService", DisplayName = "Adobe Desktop Service", Publisher = "Adobe Systems", Description = "Synchronizes Adobe cloud assets and licensing.", SafetyLevel = ProcessSafetyLevel.SafeToTerminate, SafeToEnd = true, Recommendation = "Safe to end if you are not currently syncing files to Adobe Cloud." },
                new() { ProcessName = "HPSupportSolutionsFrameworkService", DisplayName = "HP Support Solutions", Publisher = "HP Inc.", Description = "Diagnostic telemetry and driver notification agent for HP hardware.", SafetyLevel = ProcessSafetyLevel.SafeToTerminate, SafeToEnd = true, Recommendation = "Safe to end. Non-essential background diagnostic helper." },
                new() { ProcessName = "HP.OmniHub", DisplayName = "HP OmniHub Utility", Publisher = "HP Inc.", Description = "OEM peripheral and accessory companion service.", SafetyLevel = ProcessSafetyLevel.SafeToTerminate, SafeToEnd = true, Recommendation = "Safe to terminate to reclaim system RAM." },
                new() { ProcessName = "GoogleCrashHandler", DisplayName = "Google Crash Reporter", Publisher = "Google LLC", Description = "Sends Google Chrome crash telemetry.", SafetyLevel = ProcessSafetyLevel.SafeToTerminate, SafeToEnd = true, Recommendation = "Safe to end. Does not affect browser browsing." },
                new() { ProcessName = "MicrosoftEdgeUpdate", DisplayName = "Edge Updater Daemon", Publisher = "Microsoft Corporation", Description = "Checks for Edge browser updates in background.", SafetyLevel = ProcessSafetyLevel.SafeToTerminate, SafeToEnd = true, Recommendation = "Safe to terminate. Edge updates automatically when opened." },

                // Developer Daemons & Server Processes (Caution / Toggleable)
                new() { ProcessName = "postgres", DisplayName = "PostgreSQL Server Engine", Publisher = "PostgreSQL Global Development Group", Description = "Background relational database server for local software development.", SafetyLevel = ProcessSafetyLevel.DeveloperDaemon, SafeToEnd = false, Recommendation = "Stop when not coding. Each worker consumes 30MB-100MB RAM." },
                new() { ProcessName = "mysqld", DisplayName = "MySQL Database Server", Publisher = "Oracle Corporation", Description = "Active database server instance.", SafetyLevel = ProcessSafetyLevel.DeveloperDaemon, SafeToEnd = false, Recommendation = "Stop when development databases are inactive." },
                new() { ProcessName = "vmmem", DisplayName = "WSL2 / Hyper-V Virtual Memory Host", Publisher = "Microsoft Corporation", Description = "Memory allocation for Windows Subsystem for Linux and Docker containers.", SafetyLevel = ProcessSafetyLevel.DeveloperDaemon, SafeToEnd = false, Recommendation = "Can consume several gigabytes. Run 'wsl --shutdown' to release all RAM." },
                new() { ProcessName = "wsl", DisplayName = "Windows Subsystem for Linux Launcher", Publisher = "Microsoft Corporation", Description = "Linux subsystem bridge.", SafetyLevel = ProcessSafetyLevel.DeveloperDaemon, SafeToEnd = false, Recommendation = "Safe to shut down when finished with Linux terminal tasks." },
                new() { ProcessName = "Docker Desktop", DisplayName = "Docker Desktop Engine", Publisher = "Docker Inc.", Description = "Container runtime daemon.", SafetyLevel = ProcessSafetyLevel.DeveloperDaemon, SafeToEnd = false, Recommendation = "Pause or exit when no containers are actively running." },
                new() { ProcessName = "node", DisplayName = "Node.js JavaScript Runtime", Publisher = "OpenJS Foundation", Description = "Local development servers, build tools (Vite, Next.js, Webpack).", SafetyLevel = ProcessSafetyLevel.DeveloperDaemon, SafeToEnd = false, Recommendation = "Verify which dev project is running before terminating." },

                // User Applications
                new() { ProcessName = "chrome", DisplayName = "Google Chrome Web Browser", Publisher = "Google LLC", Description = "Main web browser or background extension worker.", SafetyLevel = ProcessSafetyLevel.UserApplication, SafeToEnd = false, Recommendation = "Close unused browser tabs or trim working memory." },
                new() { ProcessName = "msedge", DisplayName = "Microsoft Edge Browser", Publisher = "Microsoft Corporation", Description = "Web browser tabs and background extension hosts.", SafetyLevel = ProcessSafetyLevel.UserApplication, SafeToEnd = false, Recommendation = "Use Vortex Turbo Boost to trim idle tab memory." },
                new() { ProcessName = "Discord", DisplayName = "Discord Voice & Text Chat", Publisher = "Discord Inc.", Description = "Gaming communication client.", SafetyLevel = ProcessSafetyLevel.UserApplication, SafeToEnd = false, Recommendation = "Safe to close when focusing on work or gaming performance." },
                new() { ProcessName = "Spotify", DisplayName = "Spotify Music Player", Publisher = "Spotify AB", Description = "Audio streaming player.", SafetyLevel = ProcessSafetyLevel.UserApplication, SafeToEnd = false, Recommendation = "Safe to close if music is not actively playing." }
            };

            using var trans = conn.BeginTransaction();
            foreach (var s in seeds)
            {
                using var insertCmd = conn.CreateCommand();
                insertCmd.Transaction = trans;
                insertCmd.CommandText = @"
                    INSERT OR REPLACE INTO ProcessKnowledgeBase 
                    (ProcessName, DisplayName, Publisher, Description, SafetyLevel, SafeToEnd, Recommendation)
                    VALUES (@p, @d, @pub, @desc, @lvl, @safe, @rec)";
                insertCmd.Parameters.AddWithValue("@p", s.ProcessName.ToLowerInvariant());
                insertCmd.Parameters.AddWithValue("@d", s.DisplayName);
                insertCmd.Parameters.AddWithValue("@pub", s.Publisher);
                insertCmd.Parameters.AddWithValue("@desc", s.Description);
                insertCmd.Parameters.AddWithValue("@lvl", (int)s.SafetyLevel);
                insertCmd.Parameters.AddWithValue("@safe", s.SafeToEnd ? 1 : 0);
                insertCmd.Parameters.AddWithValue("@rec", s.Recommendation);
                insertCmd.ExecuteNonQuery();
            }
            trans.Commit();
        }

        private void SeedFolderFingerprints(SqliteConnection conn)
        {
            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM FolderFingerprints";
            long count = (long)(checkCmd.ExecuteScalar() ?? 0L);
            if (count > 0) return;

            var fingerprints = new List<FolderFingerprint>
            {
                new() { Pattern = "npm-cache", OwningApplication = "Node.js (NPM)", Publisher = "OpenJS Foundation", Category = "Development", Description = "Cached tarballs of downloaded NPM packages.", Safety = CleanupSafety.SafeToCleanCache, Recommendation = "Safe to delete. Frees space; packages re-download automatically when needed." },
                new() { Pattern = ".nuget\\packages", OwningApplication = ".NET NuGet", Publisher = "Microsoft Corporation", Category = "Development", Description = "Global cache of installed .NET NuGet library packages.", Safety = CleanupSafety.SafeToCleanCache, Recommendation = "Safe to clean. Restores automatically during project builds." },
                new() { Pattern = "pip\\cache", OwningApplication = "Python PIP", Publisher = "Python Software Foundation", Category = "Development", Description = "Downloaded Python package wheels and build caches.", Safety = CleanupSafety.SafeToCleanCache, Recommendation = "Safe to purge. PIP will fetch wheels fresh when installing." },
                new() { Pattern = "Code\\Cache", OwningApplication = "Visual Studio Code", Publisher = "Microsoft Corporation", Category = "Development", Description = "V8 code cache and cached window extensions for VS Code.", Safety = CleanupSafety.SafeToCleanCache, Recommendation = "Safe to clear. Rebuilds transparently on next editor launch." },
                new() { Pattern = "Google\\Chrome\\User Data\\Default\\Cache", OwningApplication = "Google Chrome", Publisher = "Google LLC", Category = "Web Browser", Description = "Web page images, scripts, and media cached during browsing.", Safety = CleanupSafety.SafeToCleanCache, Recommendation = "Safe to clear. Often consumes 1GB-5GB of disk space." },
                new() { Pattern = "Microsoft\\Edge\\User Data\\Default\\Cache", OwningApplication = "Microsoft Edge", Publisher = "Microsoft Corporation", Category = "Web Browser", Description = "Edge browser HTTP cache and cached web documents.", Safety = CleanupSafety.SafeToCleanCache, Recommendation = "Safe to clear. Reclaims significant drive space." },
                new() { Pattern = "Spotify\\Data", OwningApplication = "Spotify Player", Publisher = "Spotify AB", Category = "Streaming Media", Description = "Locally cached music streams for offline/smooth playback.", Safety = CleanupSafety.SafeToCleanCache, Recommendation = "Safe to clear if low on disk space; music re-streams on demand." },
                new() { Pattern = "Docker\\wsl\\data", OwningApplication = "Docker Desktop / WSL2", Publisher = "Docker Inc.", Category = "Virtualization", Description = "Virtual hard disk (ext4.vhdx) holding all Docker containers and image layers.", Safety = CleanupSafety.CautionUserData, Recommendation = "Caution: Deleting will remove local Docker containers and databases." },
                new() { Pattern = "Adobe\\Common", OwningApplication = "Adobe Creative Suite", Publisher = "Adobe Systems", Description = "Shared media cache files, peak files, and conformance files.", Safety = CleanupSafety.SafeToCleanCache, Recommendation = "Safe to clean. Re-renders automatically inside Premiere/After Effects." },
                new() { Pattern = "Windows\\SoftwareDistribution\\Download", OwningApplication = "Windows Update", Publisher = "Microsoft Corporation", Category = "Operating System", Description = "Downloaded installation payload files for past Windows Updates.", Safety = CleanupSafety.SafeToDelete, Recommendation = "Safe to purge after updates have been installed." },
                new() { Pattern = "Temp", OwningApplication = "Windows OS & Applications", Publisher = "Various", Category = "Temporary Files", Description = "Temporary scratch files, leftover installers, and session caches.", Safety = CleanupSafety.SafeToDelete, Recommendation = "Safe to delete files not currently locked by running applications." }
            };

            using var trans = conn.BeginTransaction();
            foreach (var fp in fingerprints)
            {
                using var insertCmd = conn.CreateCommand();
                insertCmd.Transaction = trans;
                insertCmd.CommandText = @"
                    INSERT OR REPLACE INTO FolderFingerprints 
                    (Pattern, OwningApplication, Publisher, Category, Description, Safety, Recommendation)
                    VALUES (@pat, @app, @pub, @cat, @desc, @safe, @rec)";
                insertCmd.Parameters.AddWithValue("@pat", fp.Pattern);
                insertCmd.Parameters.AddWithValue("@app", fp.OwningApplication);
                insertCmd.Parameters.AddWithValue("@pub", fp.Publisher);
                insertCmd.Parameters.AddWithValue("@cat", fp.Category);
                insertCmd.Parameters.AddWithValue("@desc", fp.Description);
                insertCmd.Parameters.AddWithValue("@safe", (int)fp.Safety);
                insertCmd.Parameters.AddWithValue("@rec", fp.Recommendation);
                insertCmd.ExecuteNonQuery();
            }
            trans.Commit();
        }

        public ProcessRule? GetProcessRule(string processName)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ProcessName, DisplayName, Publisher, Description, SafetyLevel, SafeToEnd, Recommendation FROM ProcessKnowledgeBase WHERE ProcessName = @name";
            cmd.Parameters.AddWithValue("@name", processName.ToLowerInvariant());

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new ProcessRule
                {
                    ProcessName = reader.GetString(0),
                    DisplayName = reader.GetString(1),
                    Publisher = reader.GetString(2),
                    Description = reader.GetString(3),
                    SafetyLevel = (ProcessSafetyLevel)reader.GetInt32(4),
                    SafeToEnd = reader.GetInt32(5) == 1,
                    Recommendation = reader.GetString(6)
                };
            }
            return null;
        }

        public List<FolderFingerprint> GetAllFingerprints()
        {
            var list = new List<FolderFingerprint>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Pattern, OwningApplication, Publisher, Category, Description, Safety, Recommendation FROM FolderFingerprints";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new FolderFingerprint
                {
                    Pattern = reader.GetString(0),
                    OwningApplication = reader.GetString(1),
                    Publisher = reader.GetString(2),
                    Category = reader.GetString(3),
                    Description = reader.GetString(4),
                    Safety = (CleanupSafety)reader.GetInt32(5),
                    Recommendation = reader.GetString(6)
                });
            }
            return list;
        }

        public void LogAudit(string actionType, string target, long bytesReclaimed, string details)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO AuditLog (Timestamp, ActionType, Target, BytesReclaimed, Details)
                VALUES (@ts, @act, @tgt, @bytes, @det)";
            cmd.Parameters.AddWithValue("@ts", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@act", actionType);
            cmd.Parameters.AddWithValue("@tgt", target);
            cmd.Parameters.AddWithValue("@bytes", bytesReclaimed);
            cmd.Parameters.AddWithValue("@det", details);
            cmd.ExecuteNonQuery();
        }

        public List<AuditEntry> GetRecentAudits(int limit = 50)
        {
            var list = new List<AuditEntry>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Timestamp, ActionType, Target, BytesReclaimed, Details FROM AuditLog ORDER BY Id DESC LIMIT @limit";
            cmd.Parameters.AddWithValue("@limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new AuditEntry
                {
                    Id = reader.GetInt32(0),
                    Timestamp = DateTime.Parse(reader.GetString(1)),
                    ActionType = reader.GetString(2),
                    Target = reader.GetString(3),
                    BytesReclaimed = reader.GetInt64(4),
                    Details = reader.GetString(5)
                });
            }
            return list;
        }

        public List<AppAccessRule> GetAllAppAccessRules()
        {
            var list = new List<AppAccessRule>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ProcessName, DisplayName, ExecutablePath, AccessStatus, MuteNotifications, IsBuiltIn, Category, DateAdded FROM AppAccessRules ORDER BY ProcessName ASC";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new AppAccessRule
                {
                    ProcessName = reader.GetString(0),
                    DisplayName = reader.GetString(1),
                    ExecutablePath = reader.GetString(2),
                    AccessStatus = reader.GetString(3),
                    MuteNotifications = reader.GetInt32(4) == 1,
                    IsBuiltIn = reader.GetInt32(5) == 1,
                    Category = reader.GetString(6),
                    DateAdded = DateTime.TryParse(reader.GetString(7), out var dt) ? dt : DateTime.Now
                });
            }
            return list;
        }

        public AppAccessRule? GetAppAccessRule(string procName)
        {
            string clean = Path.GetFileName(procName).ToLowerInvariant();
            if (!clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                clean += ".exe";

            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ProcessName, DisplayName, ExecutablePath, AccessStatus, MuteNotifications, IsBuiltIn, Category, DateAdded FROM AppAccessRules WHERE ProcessName = @p LIMIT 1";
            cmd.Parameters.AddWithValue("@p", clean);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new AppAccessRule
                {
                    ProcessName = reader.GetString(0),
                    DisplayName = reader.GetString(1),
                    ExecutablePath = reader.GetString(2),
                    AccessStatus = reader.GetString(3),
                    MuteNotifications = reader.GetInt32(4) == 1,
                    IsBuiltIn = reader.GetInt32(5) == 1,
                    Category = reader.GetString(6),
                    DateAdded = DateTime.TryParse(reader.GetString(7), out var dt) ? dt : DateTime.Now
                };
            }
            return null;
        }

        public void SaveAppAccessRule(AppAccessRule rule)
        {
            string clean = Path.GetFileName(rule.ProcessName).ToLowerInvariant();
            if (!clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                clean += ".exe";

            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO AppAccessRules 
                (ProcessName, DisplayName, ExecutablePath, AccessStatus, MuteNotifications, IsBuiltIn, Category, DateAdded)
                VALUES (@proc, @disp, @path, @status, @mute, @builtin, @cat, @date)";
            cmd.Parameters.AddWithValue("@proc", clean);
            cmd.Parameters.AddWithValue("@disp", string.IsNullOrEmpty(rule.DisplayName) ? Path.GetFileNameWithoutExtension(clean) : rule.DisplayName);
            cmd.Parameters.AddWithValue("@path", rule.ExecutablePath ?? string.Empty);
            cmd.Parameters.AddWithValue("@status", rule.AccessStatus ?? "ALLOWED");
            cmd.Parameters.AddWithValue("@mute", rule.MuteNotifications ? 1 : 0);
            cmd.Parameters.AddWithValue("@builtin", rule.IsBuiltIn ? 1 : 0);
            cmd.Parameters.AddWithValue("@cat", rule.Category ?? "General Application");
            cmd.Parameters.AddWithValue("@date", rule.DateAdded.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
        }

        public void RemoveAppAccessRule(string procName)
        {
            string clean = Path.GetFileName(procName).ToLowerInvariant();
            if (!clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                clean += ".exe";

            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM AppAccessRules WHERE ProcessName = @proc";
            cmd.Parameters.AddWithValue("@proc", clean);
            cmd.ExecuteNonQuery();
        }

        public bool IsAppAllowed(string procName, string? exePath = null)
        {
            if (string.IsNullOrEmpty(procName)) return false;
            string clean = Path.GetFileName(procName).ToLowerInvariant();
            if (!clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                clean += ".exe";

            // Known system core executables are always protected & allowed
            if (clean == "explorer.exe" || clean == "svchost.exe" || clean == "csrss.exe" || clean == "lsass.exe" ||
                clean == "smss.exe" || clean == "services.exe" || clean == "wininit.exe" || clean == "winlogon.exe" ||
                clean == "dwm.exe" || clean == "vortex.exe")
            {
                return true;
            }

            var rule = GetAppAccessRule(clean);
            if (rule != null && rule.AccessStatus == "ALLOWED")
            {
                return true;
            }

            return false;
        }

        public bool IsNotificationMuted(string procName)
        {
            if (string.IsNullOrEmpty(procName)) return false;
            string clean = Path.GetFileName(procName).ToLowerInvariant();
            if (!clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                clean += ".exe";

            var rule = GetAppAccessRule(clean);
            return rule?.MuteNotifications ?? false;
        }

        #region Vault & Folder Protection Database Operations
        public List<VaultFolderModel> GetVaultFolders()
        {
            var list = new List<VaultFolderModel>();
            try
            {
                using var conn = GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id, FolderPath, FolderName, ProtectionMode, IsLocked, IsStealth, TotalSizeBytes, FileCount, EncryptedPayloadPath, CreatedAt, LastUnlockedAt FROM VaultFolders ORDER BY FolderName ASC";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new VaultFolderModel
                    {
                        Id = reader.GetInt32(0),
                        FolderPath = reader.GetString(1),
                        FolderName = reader.GetString(2),
                        ProtectionMode = reader.GetString(3),
                        IsLocked = reader.GetInt32(4) == 1,
                        IsStealth = reader.GetInt32(5) == 1,
                        TotalSizeBytes = reader.GetInt64(6),
                        FileCount = reader.GetInt32(7),
                        EncryptedPayloadPath = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                        CreatedAt = DateTime.TryParse(reader.GetString(9), out var dt) ? dt : DateTime.UtcNow,
                        LastUnlockedAt = reader.IsDBNull(10) ? null : (DateTime.TryParse(reader.GetString(10), out var dt2) ? dt2 : null)
                    });
                }
            }
            catch { }
            return list;
        }

        public VaultFolderModel? GetVaultFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return null;
            try
            {
                string norm = folderPath.TrimEnd('\\', '/');
                using var conn = GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id, FolderPath, FolderName, ProtectionMode, IsLocked, IsStealth, TotalSizeBytes, FileCount, EncryptedPayloadPath, CreatedAt, LastUnlockedAt FROM VaultFolders WHERE RTRIM(FolderPath, '\\/') = $norm COLLATE NOCASE OR FolderPath = $raw COLLATE NOCASE LIMIT 1";
                cmd.Parameters.AddWithValue("$norm", norm);
                cmd.Parameters.AddWithValue("$raw", folderPath);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    return new VaultFolderModel
                    {
                        Id = reader.GetInt32(0),
                        FolderPath = reader.GetString(1),
                        FolderName = reader.GetString(2),
                        ProtectionMode = reader.GetString(3),
                        IsLocked = reader.GetInt32(4) == 1,
                        IsStealth = reader.GetInt32(5) == 1,
                        TotalSizeBytes = reader.GetInt64(6),
                        FileCount = reader.GetInt32(7),
                        EncryptedPayloadPath = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                        CreatedAt = DateTime.TryParse(reader.GetString(9), out var dt) ? dt : DateTime.UtcNow,
                        LastUnlockedAt = reader.IsDBNull(10) ? null : (DateTime.TryParse(reader.GetString(10), out var dt2) ? dt2 : null)
                    };
                }
            }
            catch { }
            return null;
        }

        public void SaveVaultFolder(VaultFolderModel folder)
        {
            try
            {
                using var conn = GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO VaultFolders (FolderPath, FolderName, ProtectionMode, IsLocked, IsStealth, TotalSizeBytes, FileCount, EncryptedPayloadPath, CreatedAt, LastUnlockedAt)
                    VALUES ($path, $name, $mode, $locked, $stealth, $size, $files, $payload, $created, $unlocked)
                    ON CONFLICT(FolderPath) DO UPDATE SET
                        FolderName = $name,
                        ProtectionMode = $mode,
                        IsLocked = $locked,
                        IsStealth = $stealth,
                        TotalSizeBytes = $size,
                        FileCount = $files,
                        EncryptedPayloadPath = $payload,
                        LastUnlockedAt = $unlocked;
                ";
                cmd.Parameters.AddWithValue("$path", folder.FolderPath);
                cmd.Parameters.AddWithValue("$name", folder.FolderName);
                cmd.Parameters.AddWithValue("$mode", folder.ProtectionMode);
                cmd.Parameters.AddWithValue("$locked", folder.IsLocked ? 1 : 0);
                cmd.Parameters.AddWithValue("$stealth", folder.IsStealth ? 1 : 0);
                cmd.Parameters.AddWithValue("$size", folder.TotalSizeBytes);
                cmd.Parameters.AddWithValue("$files", folder.FileCount);
                cmd.Parameters.AddWithValue("$payload", folder.EncryptedPayloadPath ?? string.Empty);
                cmd.Parameters.AddWithValue("$created", folder.CreatedAt.ToString("o"));
                cmd.Parameters.AddWithValue("$unlocked", folder.LastUnlockedAt.HasValue ? folder.LastUnlockedAt.Value.ToString("o") : (object)DBNull.Value);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        public void DeleteVaultFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return;
            try
            {
                string norm = folderPath.TrimEnd('\\', '/');
                using var conn = GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM VaultFolders WHERE RTRIM(FolderPath, '\\/') = $norm COLLATE NOCASE OR FolderPath = $raw COLLATE NOCASE";
                cmd.Parameters.AddWithValue("$norm", norm);
                cmd.Parameters.AddWithValue("$raw", folderPath);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        public VaultSettingsModel GetVaultSettings()
        {
            try
            {
                using var conn = GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT IsSetup, PasswordHash, PasswordSalt, RecoveryKeyHash, AutoLockOnScreenLock, AutoLockIdleMinutes, UsbWriteBlockEnabled, UsbAllowedUntil, ShellContextMenuEnabled, WrappedMasterKeyPassword, WrappedMasterKeyRecovery FROM VaultSettings WHERE Id = 1";
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    return new VaultSettingsModel
                    {
                        IsSetup = reader.GetInt32(0) == 1,
                        PasswordHash = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        PasswordSalt = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        RecoveryKeyHash = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        AutoLockOnScreenLock = reader.GetInt32(4) == 1,
                        AutoLockIdleMinutes = reader.GetInt32(5),
                        UsbWriteBlockEnabled = reader.GetInt32(6) == 1,
                        UsbAllowedUntil = reader.IsDBNull(7) ? null : (DateTime.TryParse(reader.GetString(7), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var udt) ? udt.ToUniversalTime() : null),
                        ShellContextMenuEnabled = reader.GetInt32(8) == 1,
                        WrappedMasterKeyPassword = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                        WrappedMasterKeyRecovery = reader.IsDBNull(10) ? string.Empty : reader.GetString(10)
                    };
                }
            }
            catch { }
            return new VaultSettingsModel();
        }

        public void SaveVaultSettings(VaultSettingsModel settings)
        {
            try
            {
                using var conn = GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO VaultSettings (Id, IsSetup, PasswordHash, PasswordSalt, RecoveryKeyHash, WrappedMasterKeyPassword, WrappedMasterKeyRecovery, AutoLockOnScreenLock, AutoLockIdleMinutes, UsbWriteBlockEnabled, UsbAllowedUntil, ShellContextMenuEnabled)
                    VALUES (1, $setup, $pass, $salt, $rec, $wrapPass, $wrapRec, $screen, $idle, $usb, $usbtime, $shell)
                    ON CONFLICT(Id) DO UPDATE SET
                        IsSetup = $setup,
                        PasswordHash = $pass,
                        PasswordSalt = $salt,
                        RecoveryKeyHash = $rec,
                        WrappedMasterKeyPassword = $wrapPass,
                        WrappedMasterKeyRecovery = $wrapRec,
                        AutoLockOnScreenLock = $screen,
                        AutoLockIdleMinutes = $idle,
                        UsbWriteBlockEnabled = $usb,
                        UsbAllowedUntil = $usbtime,
                        ShellContextMenuEnabled = $shell;
                ";
                cmd.Parameters.AddWithValue("$setup", settings.IsSetup ? 1 : 0);
                cmd.Parameters.AddWithValue("$pass", settings.PasswordHash ?? string.Empty);
                cmd.Parameters.AddWithValue("$salt", settings.PasswordSalt ?? string.Empty);
                cmd.Parameters.AddWithValue("$rec", settings.RecoveryKeyHash ?? string.Empty);
                cmd.Parameters.AddWithValue("$wrapPass", settings.WrappedMasterKeyPassword ?? string.Empty);
                cmd.Parameters.AddWithValue("$wrapRec", settings.WrappedMasterKeyRecovery ?? string.Empty);
                cmd.Parameters.AddWithValue("$screen", settings.AutoLockOnScreenLock ? 1 : 0);
                cmd.Parameters.AddWithValue("$idle", settings.AutoLockIdleMinutes);
                cmd.Parameters.AddWithValue("$usb", settings.UsbWriteBlockEnabled ? 1 : 0);
                cmd.Parameters.AddWithValue("$usbtime", settings.UsbAllowedUntil.HasValue ? settings.UsbAllowedUntil.Value.ToString("o") : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$shell", settings.ShellContextMenuEnabled ? 1 : 0);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }
        #endregion
    }
}

