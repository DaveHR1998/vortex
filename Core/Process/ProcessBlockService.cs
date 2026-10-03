using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using Vortex.Core.Database;

namespace Vortex.Core.ProcessManagement
{
    public class ProcessBlockService
    {
        private readonly DatabaseService? _db;

        public static readonly HashSet<string> Whitelist = new(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "explorer.exe",
            "svchost", "svchost.exe",
            "csrss", "csrss.exe",
            "lsass", "lsass.exe",
            "smss", "smss.exe",
            "services", "services.exe",
            "wininit", "wininit.exe",
            "winlogon", "winlogon.exe",
            "dwm", "dwm.exe",
            "System", "System Idle Process",
            "taskmgr", "taskmgr.exe",
            "conhost", "conhost.exe",
            "vortex", "vortex.exe"
        };

        public class BlockedItemModel
        {
            public string ProcessName { get; set; } = string.Empty;
            public string ExecutablePath { get; set; } = string.Empty;
            public string BlockType { get; set; } = "FIREWALL"; // FIREWALL or EXECUTION
            public string BlockTypeDisplay => BlockType == "FIREWALL" ? "🌐 Network Quarantined" : "🚫 Execution Blacklisted";
            public DateTime DateBlocked { get; set; } = DateTime.Now;
        }

        public ProcessBlockService(DatabaseService? db = null)
        {
            _db = db;
        }

        public DatabaseService? Database => _db;

        public bool IsProtected(string procName)
        {
            string clean = Path.GetFileNameWithoutExtension(procName);
            return Whitelist.Contains(clean) || Whitelist.Contains(clean + ".exe");
        }

        public (bool Success, string Message) AllowApp(string procName, string exePath = "", string displayName = "", string category = "User Application", bool muteNotifications = true)
        {
            string cleanName = Path.GetFileName(procName);
            if (!cleanName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                cleanName += ".exe";

            // If the app was quarantined in firewall, unblock it
            UnblockInternet(cleanName);
            UnblockExecution(cleanName);

            var rule = new AppAccessRule
            {
                ProcessName = cleanName,
                DisplayName = string.IsNullOrEmpty(displayName) ? Path.GetFileNameWithoutExtension(cleanName) : displayName,
                ExecutablePath = exePath,
                AccessStatus = "ALLOWED",
                MuteNotifications = muteNotifications,
                IsBuiltIn = false,
                Category = category,
                DateAdded = DateTime.Now
            };

            if (_db != null)
            {
                _db.SaveAppAccessRule(rule);
            }

            return (true, $"'{cleanName}' is now marked as Allowed & Trusted. Network alerts are bypassed.");
        }

        public (bool Success, string Message) RevokeAllowApp(string procName)
        {
            string cleanName = Path.GetFileName(procName);
            if (!cleanName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                cleanName += ".exe";

            if (_db != null)
            {
                _db.RemoveAppAccessRule(cleanName);
            }

            return (true, $"Trusted status revoked for '{cleanName}'.");
        }

        public (bool Success, string Message) ToggleNotificationMute(string procName)
        {
            string cleanName = Path.GetFileName(procName);
            if (!cleanName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                cleanName += ".exe";

            if (_db != null)
            {
                var rule = _db.GetAppAccessRule(cleanName);
                if (rule != null)
                {
                    rule.MuteNotifications = !rule.MuteNotifications;
                    _db.SaveAppAccessRule(rule);
                    return (true, rule.MuteNotifications ? $"Notifications muted for '{cleanName}'." : $"Notifications enabled for '{cleanName}'.");
                }
                else
                {
                    // Create an allowed rule with muted notification
                    AllowApp(cleanName, "", Path.GetFileNameWithoutExtension(cleanName), "User Application", true);
                    return (true, $"Notifications muted for '{cleanName}'.");
                }
            }

            return (false, "Database unavailable.");
        }

        public List<AppAccessRule> GetAllowedApps()
        {
            if (_db != null)
            {
                return _db.GetAllAppAccessRules().Where(r => r.AccessStatus == "ALLOWED").ToList();
            }
            return new List<AppAccessRule>();
        }

        public List<DiscoveredAppModel> GetDiscoveredApps()
        {
            return AppDiscoveryService.DiscoverAllApplications(_db, this);
        }

        public (bool Success, string Message) ResetAppStatus(string procName)
        {
            string cleanName = Path.GetFileName(procName);
            if (!cleanName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                cleanName += ".exe";

            UnblockInternet(cleanName);
            UnblockExecution(cleanName);

            if (_db != null)
            {
                _db.RemoveAppAccessRule(cleanName);
            }

            return (true, $"Rule removed. '{cleanName}' reset to default system monitoring.");
        }

        public bool IsAppAllowed(string procName, string? exePath = null)
        {
            if (IsProtected(procName)) return true;
            if (_db != null)
            {
                return _db.IsAppAllowed(procName, exePath);
            }
            return false;
        }

        public bool IsNotificationMuted(string procName)
        {
            if (_db != null)
            {
                return _db.IsNotificationMuted(procName);
            }
            return false;
        }

        public (bool Success, string Message) ForceTerminateProcess(int pid, string procName)
        {
            if (IsProtected(procName))
            {
                return (false, $"Process '{procName}' (PID {pid}) is a protected Windows system component and cannot be terminated.");
            }

            try
            {
                // 1. Attempt .NET Process.Kill tree
                try
                {
                    using var proc = Process.GetProcessById(pid);
                    proc.Kill(true);
                    if (proc.WaitForExit(400))
                    {
                        return (true, $"Process '{procName}' (PID {pid}) terminated successfully.");
                    }
                }
                catch { }

                // 2. Kernel force termination via taskkill.exe /F /T /PID
                var psi = new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = $"/F /T /PID {pid}",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var taskkill = Process.Start(psi);
                taskkill?.WaitForExit(1500);

                return (true, $"Process '{procName}' (PID {pid}) forcibly terminated via kernel taskkill.");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to terminate PID {pid}: {ex.Message}");
            }
        }

        public (bool Success, string Message) BlockFromInternet(string procName, string exePath)
        {
            if (IsProtected(procName))
            {
                return (false, $"Process '{procName}' is a critical Windows system component and cannot be blocked.");
            }

            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                return (false, $"Cannot block '{procName}': Executable file path '{exePath}' was not found on disk.");
            }

            try
            {
                string cleanName = Path.GetFileName(exePath);
                string ruleOut = $"Vortex_Block_Out_{cleanName}";
                string ruleIn = $"Vortex_Block_In_{cleanName}";

                // Clean existing
                RunNetsh($"advfirewall firewall delete rule name=\"{ruleOut}\"");
                RunNetsh($"advfirewall firewall delete rule name=\"{ruleIn}\"");

                // Add Outbound + Inbound block
                RunNetsh($"advfirewall firewall add rule name=\"{ruleOut}\" dir=out action=block program=\"{exePath}\" enable=yes");
                RunNetsh($"advfirewall firewall add rule name=\"{ruleIn}\" dir=in action=block program=\"{exePath}\" enable=yes");

                SaveBlockedItem(cleanName, exePath, "FIREWALL");

                return (true, $"'{cleanName}' is now PERMANENTLY blocked from the network via Windows Firewall.");
            }
            catch (Exception ex)
            {
                return (false, $"Firewall quarantine failed: {ex.Message}");
            }
        }

        public (bool Success, string Message) UnblockInternet(string procName)
        {
            try
            {
                string cleanName = Path.GetFileName(procName);
                if (!cleanName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    cleanName += ".exe";

                string ruleOut = $"Vortex_Block_Out_{cleanName}";
                string ruleIn = $"Vortex_Block_In_{cleanName}";

                RunNetsh($"advfirewall firewall delete rule name=\"{ruleOut}\"");
                RunNetsh($"advfirewall firewall delete rule name=\"{ruleIn}\"");

                RemoveBlockedItem(cleanName, "FIREWALL");

                return (true, $"Network access restored for '{cleanName}'.");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to unblock network: {ex.Message}");
            }
        }

        public (bool Success, string Message) BlockExecution(string procName, string exePath)
        {
            if (IsProtected(procName))
            {
                return (false, $"Process '{procName}' is a critical Windows system component and cannot be blacklisted.");
            }

            string cleanName = Path.GetFileName(procName);
            if (!cleanName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                cleanName += ".exe";

            try
            {
                // Kill running instances
                string baseName = Path.GetFileNameWithoutExtension(cleanName);
                foreach (var p in Process.GetProcessesByName(baseName))
                {
                    try { p.Kill(true); } catch { }
                }

                // Register IFEO interception
                using var key = Registry.LocalMachine.CreateSubKey(
                    $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{cleanName}", true);

                if (key != null)
                {
                    key.SetValue("Debugger", @"%COMSPEC% /c exit", RegistryValueKind.String);
                    SaveBlockedItem(cleanName, exePath, "EXECUTION");
                    return (true, $"'{cleanName}' is now PERMANENTLY blacklisted from running. Windows will automatically intercept and terminate launch attempts.");
                }

                return (false, "Failed to open Image File Execution Options registry key.");
            }
            catch (Exception ex)
            {
                return (false, $"Execution blacklist failed: {ex.Message}");
            }
        }

        public (bool Success, string Message) UnblockExecution(string procName)
        {
            string cleanName = Path.GetFileName(procName);
            if (!cleanName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                cleanName += ".exe";

            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(
                    $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{cleanName}", false);

                RemoveBlockedItem(cleanName, "EXECUTION");
                return (true, $"Execution blacklist removed for '{cleanName}'. The program can now run normally.");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to unblock execution: {ex.Message}");
            }
        }

        public List<BlockedItemModel> GetBlockedItems()
        {
            var list = new List<BlockedItemModel>();
            string filePath = GetStoragePath();

            if (File.Exists(filePath))
            {
                try
                {
                    var lines = File.ReadAllLines(filePath);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('|');
                        if (parts.Length >= 4)
                        {
                            list.Add(new BlockedItemModel
                            {
                                ProcessName = parts[0],
                                ExecutablePath = parts[1],
                                BlockType = parts[2],
                                DateBlocked = DateTime.TryParse(parts[3], out var dt) ? dt : DateTime.Now
                            });
                        }
                    }
                }
                catch { }
            }

            return list;
        }

        private void SaveBlockedItem(string procName, string path, string type)
        {
            var list = GetBlockedItems();
            list.RemoveAll(x => x.ProcessName.Equals(procName, StringComparison.OrdinalIgnoreCase) && x.BlockType == type);
            list.Add(new BlockedItemModel
            {
                ProcessName = procName,
                ExecutablePath = path,
                BlockType = type,
                DateBlocked = DateTime.Now
            });

            PersistList(list);
        }

        private void RemoveBlockedItem(string procName, string type)
        {
            var list = GetBlockedItems();
            list.RemoveAll(x => x.ProcessName.Equals(procName, StringComparison.OrdinalIgnoreCase) && x.BlockType == type);
            PersistList(list);
        }

        private void PersistList(List<BlockedItemModel> list)
        {
            try
            {
                string filePath = GetStoragePath();
                string dir = Path.GetDirectoryName(filePath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var lines = list.Select(x => $"{x.ProcessName}|{x.ExecutablePath}|{x.BlockType}|{x.DateBlocked:O}");
                File.WriteAllLines(filePath, lines);
            }
            catch { }
        }

        private static string GetStoragePath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vortex", "blocked_rules.dat");
        }

        private static void RunNetsh(string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
        }
    }
}
