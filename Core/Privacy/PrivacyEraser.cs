using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Vortex.Core.Privacy
{
    /// <summary>
    /// Scans and wipes OS-level activity traces: Recent Files, Jump Lists,
    /// Common File Dialog (ComDlg32) MRUs, Explorer Typed Paths, Search History,
    /// Run History, Clipboard (without disabling it), PowerShell history, Thumbnail Cache, Prefetch.
    /// </summary>
    public class PrivacyEraser
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, int dwFlags);
        private const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004;

        // ─── Scan ─────────────────────────────────────────────────────────────

        public List<PrivacyTraceItem> ScanAllTraces()
        {
            var results = new List<PrivacyTraceItem>();
            results.Add(ScanClipboard());
            results.Add(ScanRecentFiles());
            results.Add(ScanJumpLists());
            results.Add(ScanCommonFileDialogs());
            results.Add(ScanTypedPaths());
            results.Add(ScanMRUKeys());
            results.Add(ScanSearchHistory());
            results.Add(ScanRunHistory());
            results.Add(ScanPowerShellHistory());
            results.Add(ScanThumbnailCache());
            results.Add(ScanPrefetch());
            return results;
        }

        private static PrivacyTraceItem ScanClipboard()
        {
            bool hasActiveContent = false;
            try
            {
                if (System.Windows.Application.Current != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        try { hasActiveContent = System.Windows.Clipboard.ContainsText() || System.Windows.Clipboard.ContainsImage(); } catch { }
                    });
                }
            }
            catch { }

            return new PrivacyTraceItem
            {
                Name        = "Clipboard & History",
                Description = "Clears active pasteboard memory and Win+V history buffer without disabling clipboard functionality",
                Location    = @"Windows.ApplicationModel.DataTransfer.Clipboard",
                ItemCount   = hasActiveContent ? 1 : 0,
                Risk        = PrivacyRiskLevel.Critical,
                IsSelected  = true,
            };
        }

        private static PrivacyTraceItem ScanRecentFiles()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Recent");
            int count = 0; long bytes = 0;
            if (Directory.Exists(dir))
            {
                foreach (var f in Directory.GetFiles(dir, "*.lnk"))
                {
                    count++;
                    try { bytes += new FileInfo(f).Length; } catch { }
                }
            }
            return new PrivacyTraceItem
            {
                Name        = "Recent Files",
                Description = "Shortcuts to recently opened documents and files visible in Quick Access and Start Menu",
                Location    = dir,
                ItemCount   = count,
                SizeBytes   = bytes,
                Risk        = PrivacyRiskLevel.High,
                IsSelected  = true,
            };
        }

        private static PrivacyTraceItem ScanJumpLists()
        {
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Recent");
            var autoDir = Path.Combine(baseDir, "AutomaticDestinations");
            var custDir = Path.Combine(baseDir, "CustomDestinations");

            int count = 0; long bytes = 0;
            foreach (var dir in new[] { autoDir, custDir })
            {
                if (Directory.Exists(dir))
                {
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        count++;
                        try { bytes += new FileInfo(f).Length; } catch { }
                    }
                }
            }

            return new PrivacyTraceItem
            {
                Name        = "Taskbar Jump Lists",
                Description = "Recent files shown when right-clicking pinned taskbar icons (Chrome, Word, VS Code, Explorer)",
                Location    = @"%AppData%\Microsoft\Windows\Recent\*Destinations",
                ItemCount   = count,
                SizeBytes   = bytes,
                Risk        = PrivacyRiskLevel.High,
                IsSelected  = true,
            };
        }

        private static PrivacyTraceItem ScanCommonFileDialogs()
        {
            int count = 0;
            try
            {
                using var openSave = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\OpenSavePidlMRU");
                if (openSave != null) count += CountRegistryValues(openSave, deep: true);

                using var lastVisited = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\LastVisitedPidlMRU");
                if (lastVisited != null) count += lastVisited.ValueCount;

                using var lastVisitedLegacy = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\LastVisitedPidlMRULegacy");
                if (lastVisitedLegacy != null) count += lastVisitedLegacy.ValueCount;
            }
            catch { }

            return new PrivacyTraceItem
            {
                Name        = "Common File Dialog History",
                Description = "Forensic trail of all files and folders accessed via Open/Save dialogs across all applications",
                Location    = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32",
                ItemCount   = count,
                Risk        = PrivacyRiskLevel.High,
                IsSelected  = true,
            };
        }

        private static PrivacyTraceItem ScanTypedPaths()
        {
            int count = 0;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths");
                if (key != null) count = key.ValueCount;
            }
            catch { }

            return new PrivacyTraceItem
            {
                Name        = "Explorer Typed Paths",
                Description = "Folder paths manually typed into the File Explorer address bar dropdown",
                Location    = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths",
                ItemCount   = count,
                Risk        = PrivacyRiskLevel.Medium,
                IsSelected  = true,
            };
        }

        private static PrivacyTraceItem ScanMRUKeys()
        {
            int count = 0;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs");
                if (key != null) count = CountRegistryValues(key, deep: true);
            }
            catch { }
            return new PrivacyTraceItem
            {
                Name        = "MRU (Most Recently Used)",
                Description = "Registry entries storing recently accessed file extensions used by Explorer",
                Location    = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs",
                ItemCount   = count,
                Risk        = PrivacyRiskLevel.High,
                IsSelected  = true,
            };
        }

        private static PrivacyTraceItem ScanSearchHistory()
        {
            int count = 0;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\WordWheelQuery");
                if (key != null) count = key.ValueCount;
            }
            catch { }
            return new PrivacyTraceItem
            {
                Name        = "Windows Search History",
                Description = "Search queries typed into Windows Explorer search bar and Start Menu",
                Location    = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\WordWheelQuery",
                ItemCount   = count,
                Risk        = PrivacyRiskLevel.Medium,
                IsSelected  = true,
            };
        }

        private static PrivacyTraceItem ScanRunHistory()
        {
            int count = 0;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU");
                if (key != null) count = key.ValueCount;
            }
            catch { }
            return new PrivacyTraceItem
            {
                Name        = "Run Dialog History",
                Description = "Commands typed into the Win+R Run dialog box",
                Location    = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU",
                ItemCount   = count,
                Risk        = PrivacyRiskLevel.Medium,
                IsSelected  = true,
            };
        }

        private static PrivacyTraceItem ScanPowerShellHistory()
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\PowerShell\PSReadLine\ConsoleHost_history.txt");

            int count = 0; long bytes = 0;
            try
            {
                if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    bytes = info.Length;
                    count = File.ReadLines(path).Count();
                }
            }
            catch { }

            return new PrivacyTraceItem
            {
                Name        = "PowerShell Command History",
                Description = "Plaintext log of commands typed in PowerShell & Windows Terminal (may contain credentials & paths)",
                Location    = path,
                ItemCount   = count,
                SizeBytes   = bytes,
                Risk        = PrivacyRiskLevel.Critical,
                IsSelected  = true,
            };
        }

        private static PrivacyTraceItem ScanThumbnailCache()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Windows\Explorer");
            long bytes = 0; int count = 0;
            if (Directory.Exists(dir))
            {
                foreach (var f in Directory.GetFiles(dir, "thumbcache_*.db"))
                {
                    count++;
                    try { bytes += new FileInfo(f).Length; } catch { }
                }
            }
            return new PrivacyTraceItem
            {
                Name        = "Thumbnail Cache",
                Description = "Cached thumbnails of photos and documents (unlocked databases purged; locked ones scheduled for boot cleanup)",
                Location    = dir,
                ItemCount   = count,
                SizeBytes   = bytes,
                Risk        = PrivacyRiskLevel.Low,
                IsSelected  = true,
            };
        }

        private static PrivacyTraceItem ScanPrefetch()
        {
            var dir = @"C:\Windows\Prefetch";
            long bytes = 0; int count = 0;
            try
            {
                if (Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir, "*.pf"))
                    {
                        count++;
                        try { bytes += new FileInfo(f).Length; } catch { }
                    }
            }
            catch { }
            return new PrivacyTraceItem
            {
                Name        = "Prefetch Files",
                Description = "Windows app launch cache recording execution timeline of programs on this PC",
                Location    = dir,
                ItemCount   = count,
                SizeBytes   = bytes,
                Risk        = PrivacyRiskLevel.Medium,
                IsSelected  = true,
            };
        }

        // ─── Erase ────────────────────────────────────────────────────────────

        public (int wiped, long bytesFreed) EraseSelected(IEnumerable<PrivacyTraceItem> selected)
        {
            int wiped = 0; long freed = 0;
            foreach (var item in selected.Where(i => i.IsSelected))
            {
                switch (item.Name)
                {
                    case "Clipboard & History":
                        ClearClipboard();
                        wiped++;
                        break;
                    case "Recent Files":
                        (int w1, long b1) = EraseDirectory(item.Location, "*.lnk");
                        wiped += w1;
                        freed += b1;
                        break;
                    case "Taskbar Jump Lists":
                        (int w2, long b2) = EraseJumpLists();
                        wiped += w2;
                        freed += b2;
                        break;
                    case "Common File Dialog History":
                        wiped += EraseCommonFileDialogs();
                        break;
                    case "Explorer Typed Paths":
                        wiped += EraseTypedPaths();
                        break;
                    case "MRU (Most Recently Used)":
                        wiped += EraseRegistryKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs");
                        break;
                    case "Windows Search History":
                        wiped += EraseRegistryKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\WordWheelQuery");
                        break;
                    case "Run Dialog History":
                        wiped += EraseRegistryKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU");
                        break;
                    case "PowerShell Command History":
                        (int w3, long b3) = ErasePowerShellHistory();
                        wiped += w3;
                        freed += b3;
                        break;
                    case "Thumbnail Cache":
                        (int w4, long b4) = EraseThumbnails(item.Location);
                        wiped += w4;
                        freed += b4;
                        break;
                    case "Prefetch Files":
                        (int w5, long b5) = EraseDirectory(item.Location, "*.pf");
                        wiped += w5;
                        freed += b5;
                        break;
                }
            }
            return (wiped, freed);
        }

        private static (int, long) EraseDirectory(string dir, string pattern = "*")
        {
            int count = 0; long bytes = 0;
            if (!Directory.Exists(dir)) return (0, 0);
            foreach (var f in Directory.GetFiles(dir, pattern))
            {
                try { long len = new FileInfo(f).Length; File.Delete(f); count++; bytes += len; } catch { }
            }
            return (count, bytes);
        }

        private static (int, long) EraseJumpLists()
        {
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Recent");
            var autoDir = Path.Combine(baseDir, "AutomaticDestinations");
            var custDir = Path.Combine(baseDir, "CustomDestinations");

            int totalWiped = 0; long totalBytes = 0;
            foreach (var dir in new[] { autoDir, custDir })
            {
                if (Directory.Exists(dir))
                {
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        try
                        {
                            long len = new FileInfo(f).Length;
                            File.Delete(f);
                            totalWiped++;
                            totalBytes += len;
                        }
                        catch { }
                    }
                }
            }
            return (totalWiped, totalBytes);
        }

        private static int EraseCommonFileDialogs()
        {
            int wiped = 0;
            wiped += EraseRegistryKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\OpenSavePidlMRU");
            wiped += EraseRegistryKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\LastVisitedPidlMRU");
            wiped += EraseRegistryKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\LastVisitedPidlMRULegacy");
            return Math.Max(1, wiped);
        }

        private static int EraseTypedPaths()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths", writable: true);
                if (key != null)
                {
                    var valueNames = key.GetValueNames();
                    int count = valueNames.Length;
                    foreach (var name in valueNames)
                    {
                        try { key.DeleteValue(name, false); } catch { }
                    }
                    return Math.Max(1, count);
                }
            }
            catch { }
            return 0;
        }

        private static (int, long) ErasePowerShellHistory()
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\PowerShell\PSReadLine\ConsoleHost_history.txt");

            if (!File.Exists(path)) return (0, 0);
            try
            {
                long bytes = new FileInfo(path).Length;
                int count = File.ReadLines(path).Count();
                File.WriteAllText(path, string.Empty);
                File.Delete(path);
                return (count, bytes);
            }
            catch
            {
                try
                {
                    File.WriteAllText(path, string.Empty);
                    return (1, 0);
                }
                catch { }
            }
            return (0, 0);
        }

        private static (int, long) EraseThumbnails(string dir)
        {
            int count = 0; long bytes = 0;
            if (!Directory.Exists(dir)) return (0, 0);
            foreach (var f in Directory.GetFiles(dir, "thumbcache_*.db"))
            {
                try
                {
                    long len = new FileInfo(f).Length;
                    File.Delete(f);
                    count++;
                    bytes += len;
                }
                catch
                {
                    try
                    {
                        if (MoveFileEx(f, null, MOVEFILE_DELAY_UNTIL_REBOOT))
                        {
                            count++;
                        }
                    }
                    catch { }
                }
            }
            return (count, bytes);
        }

        private static int EraseRegistryKey(string subKey)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(subKey);
                int count = key != null ? Math.Max(1, key.ValueCount + key.SubKeyCount) : 0;
                Registry.CurrentUser.DeleteSubKeyTree(subKey, false);
                return count;
            }
            catch { return 0; }
        }

        private static void ClearClipboard()
        {
            try
            {
                // 1. Clear active clipboard in-memory buffer
                if (System.Windows.Application.Current != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        try { System.Windows.Clipboard.Clear(); } catch { }
                    });
                }
                else
                {
                    try { System.Windows.Clipboard.Clear(); } catch { }
                }

                // 2. Clear Win+V history buffer via WinRT API
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -Command \"try { [Windows.ApplicationModel.DataTransfer.Clipboard, Windows, ContentType = WindowsRuntime]::ClearHistory() } catch { }\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                };
                proc.Start();
                proc.WaitForExit(3000);

                // 3. Ensure Clipboard History remains enabled in Registry (never delete the configuration key!)
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Clipboard");
                if (key != null)
                {
                    key.SetValue("EnableClipboardHistory", 1, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        private static int CountRegistryValues(RegistryKey key, bool deep)
        {
            int count = key.ValueCount;
            if (deep)
            {
                foreach (var sub in key.GetSubKeyNames())
                {
                    try
                    {
                        using var s = key.OpenSubKey(sub);
                        if (s != null) count += s.ValueCount;
                    }
                    catch { }
                }
            }
            return count;
        }
    }
}
