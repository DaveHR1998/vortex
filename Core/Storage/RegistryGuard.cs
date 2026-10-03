using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using Vortex.Core.Database;

namespace Vortex.Core.Storage
{
    public class RegistryIssueModel
    {
        public string KeyPath { get; set; } = string.Empty;
        public string? ValueName { get; set; }
        public string IssueType { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public bool IsSelected { get; set; } = true;
    }

    public class RegistryGuard
    {
        private readonly DatabaseService _db;
        private readonly string _backupDir;

        public RegistryGuard(DatabaseService db)
        {
            _db = db;
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _backupDir = Path.Combine(appData, "Vortex", "Backups");
            if (!Directory.Exists(_backupDir))
            {
                Directory.CreateDirectory(_backupDir);
            }
        }

        public List<RegistryIssueModel> ScanRegistryIssues()
        {
            var issues = new List<RegistryIssueModel>();

            // 1. Scan HKCU & HKLM Uninstall entries for orphaned locations or missing uninstall executables
            ScanUninstallEntries(issues);

            // 2. Scan Windows Startup entries (Run & RunOnce) for missing executables
            ScanStartupEntries(issues);

            // 3. Scan App Paths for missing executables
            ScanAppPaths(issues);

            // 4. Scan Shared DLLs for missing library files
            ScanSharedDlls(issues);

            // 5. Scan Obsolete MUICache Entries
            ScanMuiCache(issues);

            return issues;
        }

        private static bool SafeFileExists(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                if (path.StartsWith(@"\\") || path.StartsWith("//")) return false;
                return File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        private static bool SafeDirectoryExists(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                if (path.StartsWith(@"\\") || path.StartsWith("//")) return false;
                return Directory.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        private void ScanUninstallEntries(List<RegistryIssueModel> issues)
        {
            string[] uninstallHives = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                foreach (var uninstallPath in uninstallHives)
                {
                    try
                    {
                        using var key = hive.OpenSubKey(uninstallPath);
                        if (key == null) continue;

                        foreach (var subName in key.GetSubKeyNames())
                        {
                            try
                            {
                                using var appKey = key.OpenSubKey(subName);
                                if (appKey == null) continue;

                                string? displayName = appKey.GetValue("DisplayName") as string;
                                string? installLocation = appKey.GetValue("InstallLocation") as string;
                                string? uninstallString = appKey.GetValue("UninstallString") as string;

                                if (string.IsNullOrEmpty(displayName)) continue;

                                // Check missing InstallLocation
                                if (!string.IsNullOrEmpty(installLocation) && installLocation.Length > 3 && !SafeDirectoryExists(installLocation))
                                {
                                    issues.Add(new RegistryIssueModel
                                    {
                                        KeyPath = $@"{hive.Name}\{uninstallPath}\{subName}",
                                        IssueType = "Orphaned Install Path",
                                        Details = $"{displayName} - Folder missing: {installLocation}"
                                    });
                                    continue;
                                }

                                // Check missing UninstallString executable
                                if (!string.IsNullOrEmpty(uninstallString))
                                {
                                    string exePath = ExtractExePath(uninstallString);
                                    if (!string.IsNullOrEmpty(exePath) && exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !SafeFileExists(exePath))
                                    {
                                        issues.Add(new RegistryIssueModel
                                        {
                                            KeyPath = $@"{hive.Name}\{uninstallPath}\{subName}",
                                            IssueType = "Dead Uninstaller Link",
                                            Details = $"{displayName} - Missing uninstaller: {exePath}"
                                        });
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
        }

        private void ScanStartupEntries(List<RegistryIssueModel> issues)
        {
            string[] runPaths = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"
            };

            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                foreach (var runPath in runPaths)
                {
                    try
                    {
                        using var key = hive.OpenSubKey(runPath);
                        if (key == null) continue;

                        foreach (var valName in key.GetValueNames())
                        {
                            try
                            {
                                string? rawCmd = key.GetValue(valName) as string;
                                if (string.IsNullOrEmpty(rawCmd)) continue;

                                string exePath = ExtractExePath(rawCmd);
                                if (!string.IsNullOrEmpty(exePath) && exePath.Contains('\\') && !SafeFileExists(exePath))
                                {
                                    issues.Add(new RegistryIssueModel
                                    {
                                        KeyPath = $@"{hive.Name}\{runPath}",
                                        ValueName = valName,
                                        IssueType = "Obsolete Startup Program",
                                        Details = $"{valName} points to missing executable: {exePath}"
                                    });
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
        }

        private void ScanAppPaths(List<RegistryIssueModel> issues)
        {
            string appPaths = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                try
                {
                    using var key = hive.OpenSubKey(appPaths);
                    if (key == null) continue;

                    foreach (var subName in key.GetSubKeyNames().Take(120))
                    {
                        try
                        {
                            using var sub = key.OpenSubKey(subName);
                            if (sub == null) continue;

                            string? defaultVal = sub.GetValue("") as string;
                            if (string.IsNullOrEmpty(defaultVal)) continue;

                            string exePath = ExtractExePath(defaultVal);
                            if (!string.IsNullOrEmpty(exePath) && exePath.Contains('\\') && !SafeFileExists(exePath))
                            {
                                issues.Add(new RegistryIssueModel
                                {
                                    KeyPath = $@"{hive.Name}\{appPaths}\{subName}",
                                    IssueType = "Invalid App Path",
                                    Details = $"{subName} executable not found: {exePath}"
                                });
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        private void ScanSharedDlls(List<RegistryIssueModel> issues)
        {
            string sharedDllsPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs";
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(sharedDllsPath);
                if (key == null) return;

                foreach (var valName in key.GetValueNames().Take(120))
                {
                    try
                    {
                        if (valName.Contains('\\') && !SafeFileExists(valName))
                        {
                            issues.Add(new RegistryIssueModel
                            {
                                KeyPath = $@"HKEY_LOCAL_MACHINE\{sharedDllsPath}",
                                ValueName = valName,
                                IssueType = "Missing Shared Library",
                                Details = $"Missing registered library file: {valName}"
                            });
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void ScanMuiCache(List<RegistryIssueModel> issues)
        {
            string[] muiPaths = new[]
            {
                @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache"
            };

            foreach (var hive in new[] { Registry.CurrentUser })
            {
                foreach (var muiPath in muiPaths)
                {
                    try
                    {
                        using var key = hive.OpenSubKey(muiPath);
                        if (key == null) continue;

                        foreach (var valName in key.GetValueNames().Take(150))
                        {
                            try
                            {
                                int dotIdx = valName.IndexOf(".ApplicationCompany", StringComparison.OrdinalIgnoreCase);
                                if (dotIdx < 0) dotIdx = valName.IndexOf(".FriendlyAppName", StringComparison.OrdinalIgnoreCase);

                                string candidateExe = dotIdx > 0 ? valName[..dotIdx] : valName;
                                if (candidateExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && candidateExe.Contains('\\'))
                                {
                                    if (!SafeFileExists(candidateExe))
                                    {
                                        issues.Add(new RegistryIssueModel
                                        {
                                            KeyPath = $@"{hive.Name}\{muiPath}",
                                            ValueName = valName,
                                            IssueType = "Obsolete MUICache Link",
                                            Details = $"Cached application title for deleted program: {Path.GetFileName(candidateExe)}"
                                        });
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
        }

        private static string ExtractExePath(string raw)
        {
            string trimmed = raw.Trim();
            if (trimmed.StartsWith('"'))
            {
                int endQuote = trimmed.IndexOf('"', 1);
                if (endQuote > 1) return trimmed.Substring(1, endQuote - 1);
            }

            int exeIdx = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIdx > 0)
            {
                return trimmed.Substring(0, exeIdx + 4).Trim('"', ' ');
            }

            int spaceIdx = trimmed.IndexOf(' ');
            if (spaceIdx > 0)
            {
                string firstWord = trimmed[..spaceIdx];
                if (firstWord.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return firstWord;
            }

            return trimmed;
        }

        public string BackupRegistryKey(string fullKeyPath)
        {
            string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupFile = Path.Combine(_backupDir, $"backup_{timeStamp}.reg");

            try
            {
                var psi = new ProcessStartInfo("reg.exe", $"export \"{fullKeyPath}\" \"{backupFile}\" /y")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to export reg key: {ex.Message}");
            }

            return backupFile;
        }

        public int CleanSelectedIssues(IEnumerable<RegistryIssueModel> issues)
        {
            int cleaned = 0;
            foreach (var issue in issues)
            {
                if (!issue.IsSelected) continue;

                try
                {
                    BackupRegistryKey(issue.KeyPath);

                    // Parse hive and subkey
                    string path = issue.KeyPath;
                    int slashIdx = path.IndexOf('\\');
                    if (slashIdx <= 0) continue;

                    string hiveName = path[..slashIdx];
                    string subKey = path[(slashIdx + 1)..];

                    RegistryKey? root = hiveName switch
                    {
                        "HKEY_CURRENT_USER" => Registry.CurrentUser,
                        "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
                        _ => null
                    };

                    if (root == null) continue;

                    // If valueName is specified, delete the value
                    if (!string.IsNullOrEmpty(issue.ValueName))
                    {
                        using var valKey = root.OpenSubKey(subKey, true);
                        if (valKey != null)
                        {
                            valKey.DeleteValue(issue.ValueName, false);
                            cleaned++;
                        }
                    }
                    else
                    {
                        // Otherwise delete the subkey tree
                        int lastSlash = subKey.LastIndexOf('\\');
                        if (lastSlash > 0)
                        {
                            string parentPath = subKey[..lastSlash];
                            string childName = subKey[(lastSlash + 1)..];

                            using var parentKey = root.OpenSubKey(parentPath, true);
                            if (parentKey != null)
                            {
                                parentKey.DeleteSubKeyTree(childName, false);
                                cleaned++;
                            }
                        }
                    }
                }
                catch { }
            }

            _db.LogAudit("RegistryClean", "Orphaned Keys", cleaned, $"Cleaned {cleaned} obsolete registry keys");
            return cleaned;
        }
    }
}
