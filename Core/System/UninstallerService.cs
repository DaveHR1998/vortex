using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Vortex.Core.Database;

namespace Vortex.Core.SystemManagement
{
    public class InstalledAppModel : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public string DisplayName { get; set; } = string.Empty;
        public string DisplayVersion { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public string InstallDate { get; set; } = string.Empty;
        public string InstallLocation { get; set; } = string.Empty;
        public string UninstallString { get; set; } = string.Empty;
        public string QuietUninstallString { get; set; } = string.Empty;
        public string RegistryKeyName { get; set; } = string.Empty;
        public RegistryHive RegistryHive { get; set; } = RegistryHive.LocalMachine;
        public string RegistryPath { get; set; } = string.Empty;
        public long EstimatedSizeBytes { get; set; }

        public string EstimatedSizeFormatted
        {
            get
            {
                if (EstimatedSizeBytes <= 0) return "< 1 MB";
                if (EstimatedSizeBytes >= 1024L * 1024L * 1024L)
                    return $"{EstimatedSizeBytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
                return $"{EstimatedSizeBytes / (1024.0 * 1024.0):F1} MB";
            }
        }

        public string IconEmoji
        {
            get
            {
                string name = DisplayName.ToLowerInvariant();
                if (name.Contains("chrome") || name.Contains("edge") || name.Contains("firefox") || name.Contains("brave") || name.Contains("browser")) return "🌐";
                if (name.Contains("code") || name.Contains("studio") || name.Contains("git") || name.Contains("python") || name.Contains("node")) return "💻";
                if (name.Contains("discord") || name.Contains("slack") || name.Contains("telegram") || name.Contains("teams") || name.Contains("zoom")) return "💬";
                if (name.Contains("steam") || name.Contains("epic") || name.Contains("game") || name.Contains("riot")) return "🎮";
                if (name.Contains("adobe") || name.Contains("photoshop") || name.Contains("media") || name.Contains("spotify") || name.Contains("vlc")) return "🎨";
                if (name.Contains("driver") || name.Contains("intel") || name.Contains("nvidia") || name.Contains("amd") || name.Contains("realtek")) return "⚙️";
                return "📦";
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propName) =>
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propName));
    }

    public class LeftoverFileItem
    {
        public string Path { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty; // AppData, ProgramFiles, ProgramData, UserProfile
        public long SizeBytes { get; set; }
        public string SizeFormatted => SizeBytes >= 1024 * 1024 ? $"{SizeBytes / (1024.0 * 1024.0):F1} MB" : $"{SizeBytes / 1024.0:F1} KB";
    }

    public class LeftoverRegistryItem
    {
        public string Hive { get; set; } = string.Empty;
        public string SubKey { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class DeepUninstallPreview
    {
        public InstalledAppModel App { get; set; } = new();
        public List<LeftoverFileItem> LeftoverFiles { get; set; } = new();
        public List<LeftoverRegistryItem> LeftoverRegistry { get; set; } = new();
        public long TotalResidualBytes => LeftoverFiles.Sum(f => f.SizeBytes);
        public string TotalResidualSizeFormatted
        {
            get
            {
                long bytes = TotalResidualBytes;
                if (bytes >= 1024L * 1024L * 1024L)
                    return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
                return $"{bytes / (1024.0 * 1024.0):F1} MB";
            }
        }
    }

    public class UninstallerService
    {
        private readonly DatabaseService _db;
        private static readonly HashSet<string> SystemFolderWhitelist = new(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft", "Windows", "System32", "SysWOW64", "Common Files", "Windows Defender",
            "Internet Explorer", "Windows NT", "WindowsPowerShell", "Packages", "Temp"
        };

        public UninstallerService(DatabaseService db)
        {
            _db = db;
        }

        public List<InstalledAppModel> GetInstalledApplications()
        {
            var apps = new Dictionary<string, InstalledAppModel>(StringComparer.OrdinalIgnoreCase);

            var registryTargets = new (RegistryHive Hive, string SubKey)[]
            {
                (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
            };

            foreach (var (hive, subKeyPath) in registryTargets)
            {
                try
                {
                    using var rootKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                    using var uninstallKey = rootKey.OpenSubKey(subKeyPath);
                    if (uninstallKey == null) continue;

                    foreach (var keyName in uninstallKey.GetSubKeyNames())
                    {
                        try
                        {
                            using var appKey = uninstallKey.OpenSubKey(keyName);
                            if (appKey == null) continue;

                            string? displayName = appKey.GetValue("DisplayName")?.ToString()?.Trim();
                            if (string.IsNullOrWhiteSpace(displayName)) continue;

                            // Skip Windows Updates and system components
                            int isSystemComponent = (int)(appKey.GetValue("SystemComponent") ?? 0);
                            if (isSystemComponent == 1) continue;

                            string? parentKey = appKey.GetValue("ParentKeyName")?.ToString();
                            if (!string.IsNullOrEmpty(parentKey)) continue;

                            string? uninstallStr = appKey.GetValue("UninstallString")?.ToString();
                            string? quietUninstall = appKey.GetValue("QuietUninstallString")?.ToString();
                            if (string.IsNullOrEmpty(uninstallStr) && string.IsNullOrEmpty(quietUninstall)) continue;

                            string publisher = appKey.GetValue("Publisher")?.ToString()?.Trim() ?? "Independent Developer";
                            string version = appKey.GetValue("DisplayVersion")?.ToString()?.Trim() ?? "1.0.0";
                            string installDate = appKey.GetValue("InstallDate")?.ToString()?.Trim() ?? string.Empty;
                            if (installDate.Length == 8 && int.TryParse(installDate, out _))
                            {
                                installDate = $"{installDate.Substring(0, 4)}-{installDate.Substring(4, 2)}-{installDate.Substring(6, 2)}";
                            }
                            else if (string.IsNullOrEmpty(installDate))
                            {
                                installDate = "Installed";
                            }

                            string location = appKey.GetValue("InstallLocation")?.ToString()?.Trim() ?? string.Empty;
                            long estSizeBytes = 0;
                            object? sizeObj = appKey.GetValue("EstimatedSize");
                            if (sizeObj != null && long.TryParse(sizeObj.ToString(), out long sizeKb))
                            {
                                estSizeBytes = sizeKb * 1024L;
                            }

                            // If estimated size wasn't in registry, estimate from install location
                            if (estSizeBytes <= 0 && !string.IsNullOrEmpty(location) && Directory.Exists(location))
                            {
                                estSizeBytes = FastDirectorySize(location);
                            }

                            string uniqueKey = $"{displayName}_{publisher}";
                            if (!apps.ContainsKey(uniqueKey))
                            {
                                apps[uniqueKey] = new InstalledAppModel
                                {
                                    DisplayName = displayName,
                                    DisplayVersion = version,
                                    Publisher = publisher,
                                    InstallDate = installDate,
                                    InstallLocation = location,
                                    UninstallString = uninstallStr ?? string.Empty,
                                    QuietUninstallString = quietUninstall ?? string.Empty,
                                    RegistryKeyName = keyName,
                                    RegistryHive = hive,
                                    RegistryPath = $@"{subKeyPath}\{keyName}",
                                    EstimatedSizeBytes = estSizeBytes
                                };
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            return apps.Values.OrderBy(a => a.DisplayName).ToList();
        }

        public DeepUninstallPreview ScanLeftovers(InstalledAppModel app)
        {
            var preview = new DeepUninstallPreview { App = app };
            string appName = CleanAppName(app.DisplayName);
            string publisher = CleanAppName(app.Publisher);

            // 1. Scan filesystem paths for leftovers
            var searchRoots = new (string Path, string Category)[]
            {
                (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppData Local"),
                (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AppData Roaming"),
                (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow"), "AppData LocalLow"),
                (Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ProgramData"),
                (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Program Files"),
                (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Program Files (x86)"),
                (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "User Profile")
            };

            foreach (var (rootPath, category) in searchRoots)
            {
                if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath)) continue;

                try
                {
                    var dirs = Directory.GetDirectories(rootPath);
                    foreach (var dir in dirs)
                    {
                        string dirName = Path.GetFileName(dir);
                        if (SystemFolderWhitelist.Contains(dirName)) continue;

                        bool isMatch = IsResidualMatch(dirName, appName, publisher);
                        if (isMatch)
                        {
                            long size = FastDirectorySize(dir);
                            preview.LeftoverFiles.Add(new LeftoverFileItem
                            {
                                Path = dir,
                                Category = category,
                                SizeBytes = size
                            });
                        }
                    }
                }
                catch { }
            }

            // Also check InstallLocation if it still exists
            if (!string.IsNullOrEmpty(app.InstallLocation) && Directory.Exists(app.InstallLocation))
            {
                if (!preview.LeftoverFiles.Any(f => f.Path.Equals(app.InstallLocation, StringComparison.OrdinalIgnoreCase)))
                {
                    preview.LeftoverFiles.Add(new LeftoverFileItem
                    {
                        Path = app.InstallLocation,
                        Category = "Install Directory",
                        SizeBytes = FastDirectorySize(app.InstallLocation)
                    });
                }
            }

            // 2. Scan Registry Leftovers
            var regRoots = new (RegistryHive Hive, string SubKey)[]
            {
                (RegistryHive.CurrentUser, @"Software"),
                (RegistryHive.LocalMachine, @"Software"),
                (RegistryHive.LocalMachine, @"Software\WOW6432Node")
            };

            foreach (var (hive, rootSubKey) in regRoots)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                    using var subKey = baseKey.OpenSubKey(rootSubKey);
                    if (subKey == null) continue;

                    foreach (var keyName in subKey.GetSubKeyNames())
                    {
                        if (SystemFolderWhitelist.Contains(keyName)) continue;

                        if (IsResidualMatch(keyName, appName, publisher))
                        {
                            preview.LeftoverRegistry.Add(new LeftoverRegistryItem
                            {
                                Hive = hive.ToString(),
                                SubKey = $@"{rootSubKey}\{keyName}",
                                Description = $"Orphaned Software Settings: {keyName}"
                            });
                        }
                    }
                }
                catch { }
            }

            return preview;
        }

        public (bool success, string message) ExecuteNativeUninstall(InstalledAppModel app, bool silent = false)
        {
            try
            {
                // 1. Terminate any active process from this app
                TerminateAppProcesses(app);

                // 2. Determine uninstaller command and arguments
                string cmd = !string.IsNullOrEmpty(app.QuietUninstallString) && silent 
                    ? app.QuietUninstallString 
                    : app.UninstallString;

                if (string.IsNullOrWhiteSpace(cmd))
                    cmd = app.UninstallString;

                if (string.IsNullOrWhiteSpace(cmd))
                    return (false, "No valid uninstall command found in registry.");

                string fileName;
                string args = string.Empty;

                // Handle quoted paths: "C:\Program Files\App\uninstall.exe" /arg
                if (cmd.StartsWith("\""))
                {
                    int closeIdx = cmd.IndexOf('"', 1);
                    if (closeIdx > 0)
                    {
                        fileName = cmd.Substring(1, closeIdx - 1);
                        args = cmd.Substring(closeIdx + 1).Trim();
                    }
                    else
                    {
                        fileName = cmd.Trim('"');
                    }
                }
                else
                {
                    int spaceIdx = cmd.IndexOf(' ');
                    if (spaceIdx > 0 && !File.Exists(cmd))
                    {
                        fileName = cmd.Substring(0, spaceIdx);
                        args = cmd.Substring(spaceIdx + 1).Trim();
                    }
                    else
                    {
                        fileName = cmd;
                    }
                }

                // If silent was requested and command is MsiExec, add /qn
                if (silent && fileName.ToLowerInvariant().Contains("msiexec"))
                {
                    if (!args.Contains("/qn") && !args.Contains("/quiet"))
                        args += " /qn /norestart";
                }

                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit(60000); // Wait up to 1 min
                    return (true, $"Native uninstaller for '{app.DisplayName}' finished with exit code {proc.ExitCode}.");
                }

                return (true, $"Executed uninstaller for '{app.DisplayName}'.");
            }
            catch (Exception ex)
            {
                return (false, $"Uninstaller execution failed: {ex.Message}");
            }
        }

        public (int filesRemoved, long bytesFreed, int regKeysRemoved) CleanLeftovers(List<LeftoverFileItem> files, List<LeftoverRegistryItem> registry)
        {
            int filesRemoved = 0;
            long bytesFreed = 0;
            int regKeysRemoved = 0;

            // 1. Clean residual files/directories
            foreach (var item in files)
            {
                try
                {
                    if (Directory.Exists(item.Path))
                    {
                        // Remove read-only attributes
                        NormalizeAttributes(item.Path);
                        Directory.Delete(item.Path, true);
                        bytesFreed += item.SizeBytes;
                        filesRemoved++;
                    }
                    else if (File.Exists(item.Path))
                    {
                        File.SetAttributes(item.Path, FileAttributes.Normal);
                        File.Delete(item.Path);
                        bytesFreed += item.SizeBytes;
                        filesRemoved++;
                    }
                }
                catch { }
            }

            // 2. Clean residual registry keys
            foreach (var item in registry)
            {
                try
                {
                    var hive = item.Hive.Contains("CurrentUser") ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
                    using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                    baseKey.DeleteSubKeyTree(item.SubKey, false);
                    regKeysRemoved++;
                }
                catch { }
            }

            _db.LogAudit("DeepUninstaller", "LeftoversClean", bytesFreed, $"Purged {filesRemoved} residual folders ({bytesFreed / (1024.0 * 1024.0):F1} MB) and {regKeysRemoved} registry keys.");
            return (filesRemoved, bytesFreed, regKeysRemoved);
        }

        public (bool success, string summary) ForceUninstall(InstalledAppModel app)
        {
            try
            {
                // 1. Terminate running processes
                TerminateAppProcesses(app);

                // 2. Scan and clean all leftovers
                var preview = ScanLeftovers(app);
                var (filesRemoved, bytesFreed, regKeysRemoved) = CleanLeftovers(preview.LeftoverFiles, preview.LeftoverRegistry);

                // 3. Remove main uninstall registry entry
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(app.RegistryHive, RegistryView.Default);
                    string parentPath = Path.GetDirectoryName(app.RegistryPath) ?? string.Empty;
                    using var parentKey = baseKey.OpenSubKey(parentPath, true);
                    parentKey?.DeleteSubKeyTree(app.RegistryKeyName, false);
                }
                catch { }

                string summary = $"Force uninstalled '{app.DisplayName}': purged {filesRemoved} residual directories ({bytesFreed / (1024.0 * 1024.0):F1} MB) and removed registry footprint.";
                _db.LogAudit("DeepUninstaller", "ForceUninstall", bytesFreed, summary);
                return (true, summary);
            }
            catch (Exception ex)
            {
                return (false, $"Force removal failed: {ex.Message}");
            }
        }

        private void TerminateAppProcesses(InstalledAppModel app)
        {
            string appName = CleanAppName(app.DisplayName);
            string location = app.InstallLocation;

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.Id == Process.GetCurrentProcess().Id) continue;

                    bool shouldKill = false;
                    string procName = proc.ProcessName;

                    if (procName.Contains(appName, StringComparison.OrdinalIgnoreCase) || appName.Contains(procName, StringComparison.OrdinalIgnoreCase))
                    {
                        shouldKill = true;
                    }
                    else if (!string.IsNullOrEmpty(location))
                    {
                        try
                        {
                            string? mainModule = proc.MainModule?.FileName;
                            if (!string.IsNullOrEmpty(mainModule) && mainModule.StartsWith(location, StringComparison.OrdinalIgnoreCase))
                            {
                                shouldKill = true;
                            }
                        }
                        catch { }
                    }

                    if (shouldKill)
                    {
                        proc.Kill();
                        proc.WaitForExit(3000);
                    }
                }
                catch { }
                finally { proc.Dispose(); }
            }
        }

        private static bool IsResidualMatch(string candidate, string appName, string publisher)
        {
            if (string.IsNullOrWhiteSpace(candidate) || candidate.Length < 3) return false;

            string c = candidate.ToLowerInvariant();
            string a = appName.ToLowerInvariant();
            string p = publisher.ToLowerInvariant();

            if (c == a || c == p) return true;
            if (a.Length >= 4 && c.Contains(a)) return true;
            if (p.Length >= 4 && !p.Contains("microsoft") && c.Contains(p)) return true;

            return false;
        }

        private static string CleanAppName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            // Strip version numbers, brackets, architecture (x64, x86)
            string cleaned = Regex.Replace(input, @"\s*(\bv?\d+(\.\d+)*\b|\(x\d+\)|64-bit|32-bit)\s*", " ", RegexOptions.IgnoreCase).Trim();
            return cleaned;
        }

        private static long FastDirectorySize(string path)
        {
            try
            {
                long size = 0;
                var di = new DirectoryInfo(path);
                foreach (var file in di.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    try { size += file.Length; } catch { }
                }
                return size;
            }
            catch { return 0; }
        }

        private static void NormalizeAttributes(string directoryPath)
        {
            try
            {
                var dir = new DirectoryInfo(directoryPath);
                dir.Attributes = FileAttributes.Normal;
                foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    try { file.Attributes = FileAttributes.Normal; } catch { }
                }
            }
            catch { }
        }
    }
}
