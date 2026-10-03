using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Vortex.Core.Privacy
{
    /// <summary>
    /// Scans and wipes OS-level activity traces: Recent Files, MRU keys,
    /// Search History, Run History, Clipboard, Thumbnail Cache, Prefetch.
    /// </summary>
    public class PrivacyEraser
    {
        // ─── Scan ─────────────────────────────────────────────────────────────

        public List<PrivacyTraceItem> ScanAllTraces()
        {
            var results = new List<PrivacyTraceItem>();
            results.Add(ScanRecentFiles());
            results.Add(ScanMRUKeys());
            results.Add(ScanSearchHistory());
            results.Add(ScanRunHistory());
            results.Add(ScanClipboard());
            results.Add(ScanThumbnailCache());
            results.Add(ScanPrefetch());
            return results;
        }

        private static PrivacyTraceItem ScanRecentFiles()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Recent");
            int count = 0; long bytes = 0;
            if (Directory.Exists(dir))
            {
                foreach (var f in EnumerateSafe(dir))
                {
                    count++;
                    try { bytes += new FileInfo(f).Length; } catch { }
                }
            }
            return new PrivacyTraceItem
            {
                Name        = "Recent Files",
                Description = "Shortcuts to recently opened documents, images, and files visible in the Start Menu and Quick Access",
                Location    = dir,
                ItemCount   = count,
                SizeBytes   = bytes,
                Risk        = PrivacyRiskLevel.High,
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
                Description = "Registry entries storing your recently accessed file types, used by Explorer and shell dialogs",
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
                Description = "Terms you typed into Windows Explorer search bar and the Start Menu search",
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
                Description = "Commands you previously typed into the Win+R Run dialog box",
                Location    = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU",
                ItemCount   = count,
                Risk        = PrivacyRiskLevel.Medium,
                IsSelected  = true,
            };
        }

        private static PrivacyTraceItem ScanClipboard()
        {
            bool hasContent = false;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Clipboard");
                hasContent = key != null;
            }
            catch { }
            return new PrivacyTraceItem
            {
                Name        = "Clipboard History",
                Description = "Windows clipboard history (Win+V) — may contain copied passwords, tokens, or sensitive text",
                Location    = @"HKCU\Software\Microsoft\Clipboard",
                ItemCount   = hasContent ? 1 : 0,
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
                Description = "Cached thumbnails of photos and documents — reveals which image files you have accessed",
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
                Description = "Windows app launch cache — reveals the names of every executable ever run on this PC",
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
                    case "Recent Files":         (int w1, long b1) = EraseDirectory(item.Location, "*.lnk"); wiped += w1; freed += b1; break;
                    case "MRU (Most Recently Used)": EraseRegistryKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs"); wiped++; break;
                    case "Windows Search History":   EraseRegistryKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\WordWheelQuery"); wiped++; break;
                    case "Run Dialog History":        EraseRegistryKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU"); wiped++; break;
                    case "Clipboard History":         ClearClipboard(); wiped++; break;
                    case "Thumbnail Cache":           (int w5, long b5) = EraseThumbnails(item.Location); wiped += w5; freed += b5; break;
                    case "Prefetch Files":            (int w6, long b6) = EraseDirectory(item.Location, "*.pf"); wiped += w6; freed += b6; break;
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

        private static (int, long) EraseThumbnails(string dir)
        {
            int count = 0; long bytes = 0;
            if (!Directory.Exists(dir)) return (0, 0);
            foreach (var f in Directory.GetFiles(dir, "thumbcache_*.db"))
            {
                try { long len = new FileInfo(f).Length; File.Delete(f); count++; bytes += len; } catch { }
            }
            return (count, bytes);
        }

        private static void EraseRegistryKey(string subKey)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(subKey, false); } catch { }
        }

        private static void ClearClipboard()
        {
            try
            {
                // Clear in-memory clipboard
                System.Windows.Application.Current.Dispatcher.Invoke(System.Windows.Clipboard.Clear);
                // Clear cloud clipboard history registry entry
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Clipboard", false);
            }
            catch { }
        }

        private static int CountRegistryValues(RegistryKey key, bool deep)
        {
            int count = key.ValueCount;
            if (deep) foreach (var sub in key.GetSubKeyNames()) try { using var s = key.OpenSubKey(sub); if (s != null) count += s.ValueCount; } catch { }
            return count;
        }

        private static IEnumerable<string> EnumerateSafe(string dir)
        {
            try { return Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly); } catch { return Array.Empty<string>(); }
        }
    }
}
