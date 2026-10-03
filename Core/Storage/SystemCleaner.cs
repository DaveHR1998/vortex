using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vortex.Core.Database;

namespace Vortex.Core.Storage
{
    public class JunkCategoryScanResult
    {
        public string CategoryName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> TargetPaths { get; set; } = new();
        public long TotalBytes { get; set; }
        public int FileCount { get; set; }
        public bool IsSelected { get; set; } = true;
        public string SizeString
        {
            get
            {
                if (TotalBytes <= 0) return "0 B (Clean)";
                if (TotalBytes > 1024L * 1024L * 1024L)
                    return $"{TotalBytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
                if (TotalBytes > 1024L * 1024L)
                    return $"{TotalBytes / (1024.0 * 1024.0):F1} MB";
                return $"{TotalBytes / 1024.0:F1} KB";
            }
        }
    }

    public class SystemCleaner
    {
        private readonly DatabaseService _db;

        public SystemCleaner(DatabaseService db)
        {
            _db = db;
        }

        public List<JunkCategoryScanResult> ScanAllJunkCategories()
        {
            var results = new List<JunkCategoryScanResult>();

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // 1. Temporary OS & App Files
            results.Add(ScanCategory(
                "Windows & User Temp",
                "Temporary scratch files, installer caches, and lingering staging directories.",
                new[] { Path.GetTempPath(), Path.Combine(winDir, "Temp") }
            ));

            // 2. Windows Crash Dumps & Diagnostics
            results.Add(ScanCategory(
                "Crash Dumps & Error Reports",
                "Crash memory dumps, WER telemetry queues, and Windows minidumps.",
                new[]
                {
                    Path.Combine(localAppData, "CrashDumps"),
                    Path.Combine(localAppData, "Microsoft", "Windows", "WER"),
                    Path.Combine(winDir, "Minidump")
                }
            ));

            // 3. Google Chrome Cache
            var chromePaths = new List<string>
            {
                Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache"),
                Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Code Cache"),
                Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "GPUCache"),
                Path.Combine(localAppData, "Google", "Chrome", "User Data", "ShaderCache")
            };
            // Add Profile folders if present
            string chromeUserData = Path.Combine(localAppData, "Google", "Chrome", "User Data");
            if (Directory.Exists(chromeUserData))
            {
                try
                {
                    foreach (var profileDir in Directory.EnumerateDirectories(chromeUserData, "Profile *"))
                    {
                        chromePaths.Add(Path.Combine(profileDir, "Cache"));
                        chromePaths.Add(Path.Combine(profileDir, "Code Cache"));
                        chromePaths.Add(Path.Combine(profileDir, "GPUCache"));
                    }
                }
                catch { }
            }
            results.Add(ScanCategory(
                "Google Chrome Cache",
                "Browser web cache, font caches, JavaScript code cache, and GPU shader buffers.",
                chromePaths.ToArray()
            ));

            // 4. Microsoft Edge Cache
            var edgePaths = new List<string>
            {
                Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache"),
                Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Code Cache"),
                Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "GPUCache"),
                Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "ShaderCache")
            };
            string edgeUserData = Path.Combine(localAppData, "Microsoft", "Edge", "User Data");
            if (Directory.Exists(edgeUserData))
            {
                try
                {
                    foreach (var profileDir in Directory.EnumerateDirectories(edgeUserData, "Profile *"))
                    {
                        edgePaths.Add(Path.Combine(profileDir, "Cache"));
                        edgePaths.Add(Path.Combine(profileDir, "Code Cache"));
                        edgePaths.Add(Path.Combine(profileDir, "GPUCache"));
                    }
                }
                catch { }
            }
            results.Add(ScanCategory(
                "Microsoft Edge Cache",
                "Edge HTTP response cache, font cache, script bytecode, and rendering buffers.",
                edgePaths.ToArray()
            ));

            // 5. Windows System Caches & Explorer Thumbnails
            results.Add(ScanCategory(
                "Windows Explorer & Thumbnail Caches",
                "Cached folder thumbnails, explorer database caches, and inet cache temp files.",
                new[]
                {
                    Path.Combine(localAppData, "Microsoft", "Windows", "Explorer"),
                    Path.Combine(localAppData, "Microsoft", "Windows", "INetCache")
                }
            ));

            // 6. Windows Update Download Cache
            results.Add(ScanCategory(
                "Windows Update Cache",
                "Downloaded Windows Update installer packages, delivery optimization caches, and staging CABs.",
                new[]
                {
                    Path.Combine(winDir, "SoftwareDistribution", "Download"),
                    Path.Combine(winDir, "SoftwareDistribution", "DeliveryOptimization")
                }
            ));

            // 7. Developer & Package Caches
            results.Add(ScanCategory(
                "Developer & Package Caches",
                "Node package manager downloaded tarballs, pip download caches, and local tool temp files.",
                new[]
                {
                    Path.Combine(localAppData, "npm-cache"),
                    Path.Combine(localAppData, "pip", "cache"),
                    Path.Combine(userProfile, ".nuget", "packages", ".tools")
                }
            ));

            return results;
        }

        private JunkCategoryScanResult ScanCategory(string name, string desc, string[] paths)
        {
            var item = new JunkCategoryScanResult
            {
                CategoryName = name,
                Description = desc,
                TargetPaths = new List<string>(paths.Where(p => !string.IsNullOrWhiteSpace(p)))
            };

            foreach (var p in item.TargetPaths)
            {
                if (!Directory.Exists(p)) continue;

                foreach (var file in EnumerateFilesSafe(p))
                {
                    item.FileCount++;
                    item.TotalBytes += file.Length;
                }
            }

            // Only select by default if there are files found
            item.IsSelected = item.FileCount > 0;

            return item;
        }

        public (int deletedFiles, long freedBytes) CleanCategories(IEnumerable<JunkCategoryScanResult> selectedCategories)
        {
            int deletedCount = 0;
            long freedBytes = 0;

            foreach (var cat in selectedCategories)
            {
                if (!cat.IsSelected) continue;

                foreach (var path in cat.TargetPaths)
                {
                    if (!Directory.Exists(path)) continue;

                    foreach (var file in EnumerateFilesSafe(path))
                    {
                        try
                        {
                            long len = file.Length;
                            file.Attributes = FileAttributes.Normal;
                            file.Delete();
                            deletedCount++;
                            freedBytes += len;
                        }
                        catch
                        {
                            // In-use or locked by system/process - safely skip
                        }
                    }

                    // Attempt to delete empty subdirectories
                    CleanEmptyDirectoriesSafe(path);
                }
            }

            _db.LogAudit("SystemClean", "Junk Files", freedBytes, $"Cleaned {deletedCount} files");
            return (deletedCount, freedBytes);
        }

        private static IEnumerable<FileInfo> EnumerateFilesSafe(string rootPath, int maxDepth = 4, int maxFiles = 5000)
        {
            var dirs = new Stack<(string Path, int Depth)>();
            dirs.Push((rootPath, 0));
            int yieldedCount = 0;

            while (dirs.Count > 0 && yieldedCount < maxFiles)
            {
                var (currentDir, depth) = dirs.Pop();

                // Skip symbolic links, junction points, and reparse points to avoid circular loops
                try
                {
                    var di = new DirectoryInfo(currentDir);
                    if (di.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                }
                catch
                {
                    continue;
                }

                // 1. Enumerate files in current directory
                string[]? files = null;
                try
                {
                    files = Directory.GetFiles(currentDir);
                }
                catch { }

                if (files != null)
                {
                    foreach (var filePath in files)
                    {
                        if (yieldedCount >= maxFiles) break;

                        FileInfo? fi = null;
                        try
                        {
                            fi = new FileInfo(filePath);
                        }
                        catch { }

                        if (fi != null && fi.Exists)
                        {
                            yieldedCount++;
                            yield return fi;
                        }
                    }
                }

                // 2. Enumerate subdirectories if within depth limit
                if (depth < maxDepth)
                {
                    string[]? subDirs = null;
                    try
                    {
                        subDirs = Directory.GetDirectories(currentDir);
                    }
                    catch { }

                    if (subDirs != null)
                    {
                        foreach (var sub in subDirs)
                        {
                            dirs.Push((sub, depth + 1));
                        }
                    }
                }
            }
        }

        private static void CleanEmptyDirectoriesSafe(string rootPath)
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(rootPath))
                {
                    CleanEmptyDirectoriesSafe(dir);
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        {
                            Directory.Delete(dir, false);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
