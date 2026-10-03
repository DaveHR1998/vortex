using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using Vortex.Core.Database;

namespace Vortex.Core.Storage
{
    public class FolderProvenanceEngine
    {
        private readonly DatabaseService _db;
        private readonly Dictionary<string, (string DisplayName, string Publisher)> _installedAppsCache;

        private static readonly HashSet<string> SystemFolderWhitelist = new(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft", "Windows", "Packages", "Temp", "VirtualStore", "ConnectedDevicesPlatform",
            "History", "D3DSCache", "CrashDumps", "System32", "SoftwareDistribution", "LocalLow",
            "assembly", "System Volume Information", "Default", "Public", "All Users"
        };

        private static readonly HashSet<string> JunkExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".tmp", ".temp", ".log", ".old", ".bak", ".chk", ".dmp", ".wer", ".etl",
            ".cache", ".crdownload", ".partial", ".trace", ".1", ".2", ".3"
        };

        public FolderProvenanceEngine(DatabaseService db)
        {
            _db = db;
            _installedAppsCache = BuildInstalledAppsCache();
        }

        private Dictionary<string, (string DisplayName, string Publisher)> BuildInstalledAppsCache()
        {
            var cache = new Dictionary<string, (string DisplayName, string Publisher)>(StringComparer.OrdinalIgnoreCase);

            string[] uninstallKeys = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                foreach (var subKeyPath in uninstallKeys)
                {
                    try
                    {
                        using var key = root.OpenSubKey(subKeyPath);
                        if (key == null) continue;

                        foreach (var subKeyName in key.GetSubKeyNames())
                        {
                            try
                            {
                                using var appKey = key.OpenSubKey(subKeyName);
                                if (appKey == null) continue;

                                string? displayName = appKey.GetValue("DisplayName") as string;
                                string? publisher = appKey.GetValue("Publisher") as string ?? "Unknown Publisher";
                                string? installLocation = appKey.GetValue("InstallLocation") as string;

                                if (!string.IsNullOrEmpty(displayName))
                                {
                                    cache[displayName] = (displayName, publisher);
                                    cache[subKeyName] = (displayName, publisher);

                                    if (!string.IsNullOrEmpty(installLocation))
                                    {
                                        string folderName = Path.GetFileName(installLocation.TrimEnd('\\', '/'));
                                        if (!string.IsNullOrEmpty(folderName))
                                        {
                                            cache[folderName] = (displayName, publisher);
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }

            return cache;
        }

        public List<FolderAttributionModel> ScanAndIdentifyDirectories(string targetRootDirectory, int maxDepth = 2)
        {
            var results = new List<FolderAttributionModel>();
            if (!Directory.Exists(targetRootDirectory)) return results;

            var fingerprints = _db.GetAllFingerprints();
            var runningProcesses = GetRunningProcessesSnapshot();

            try
            {
                var dirInfo = new DirectoryInfo(targetRootDirectory);
                var subDirs = dirInfo.GetDirectories();

                foreach (var subDir in subDirs)
                {
                    try
                    {
                        string fullPath = subDir.FullName;
                        string dirName = subDir.Name;
                        string owner = GetDirectoryOwnerSafe(fullPath);

                        // 1. Analyze Folder Size and Extension Content Sampling
                        var analysis = AnalyzeFolderContentSafe(subDir, maxSampleFiles: 150);
                        long size = analysis.TotalSize;

                        // 2. Running Process Protection Check
                        var activeProcess = FindActiveRunningProcessForFolder(fullPath, dirName, runningProcesses);
                        if (activeProcess.HasValue)
                        {
                            results.Add(new FolderAttributionModel
                            {
                                Path = fullPath,
                                FolderName = dirName,
                                TotalSizeBytes = size,
                                OwningApplication = activeProcess.Value.ProcessName,
                                Publisher = "Active Running Process",
                                Category = "Process In Use",
                                OwnerAccount = owner,
                                Description = $"Folder is actively accessed by running process: {activeProcess.Value.ProcessName} (PID {activeProcess.Value.Id})",
                                Safety = CleanupSafety.RunningProcessLocked,
                                IsRunningProcessLocked = true,
                                ActiveProcessName = activeProcess.Value.ProcessName,
                                ActiveProcessId = activeProcess.Value.Id,
                                Recommendation = $"Application '{activeProcess.Value.ProcessName}' (PID {activeProcess.Value.Id}) is running in task manager. Deletion locked to prevent process crash or file corruption."
                            });
                            continue;
                        }

                        // 3. Extension Content Sampling Upgrade: if >= 90% files are .tmp, .log, .old, .bak, upgrade to SafeToDelete
                        if (analysis.SampledFilesCount >= 5 && analysis.JunkRatio >= 0.90)
                        {
                            results.Add(new FolderAttributionModel
                            {
                                Path = fullPath,
                                FolderName = dirName,
                                TotalSizeBytes = size,
                                OwningApplication = dirName,
                                Publisher = "Diagnostic & Scratch Logs",
                                Category = "Temporary / Log Junk",
                                OwnerAccount = owner,
                                Description = $"Content analysis detected {analysis.JunkRatio:P0} disposable junk files ({analysis.JunkFilesCount}/{analysis.SampledFilesCount} files).",
                                Safety = CleanupSafety.SafeToDelete,
                                Recommendation = $"Safe to delete: {analysis.JunkRatio:P0} of sampled files are temporary logs, crash dumps, or backups (.tmp, .log, .bak)."
                            });
                            continue;
                        }

                        // 4. Curated Fingerprint Pattern Match (Tier 1)
                        FolderFingerprint? matchedFp = null;
                        foreach (var fp in fingerprints)
                        {
                            if (fullPath.Contains(fp.Pattern, StringComparison.OrdinalIgnoreCase))
                            {
                                matchedFp = fp;
                                break;
                            }
                        }

                        if (matchedFp != null)
                        {
                            results.Add(new FolderAttributionModel
                            {
                                Path = fullPath,
                                FolderName = dirName,
                                TotalSizeBytes = size,
                                OwningApplication = matchedFp.OwningApplication,
                                Publisher = matchedFp.Publisher,
                                Category = matchedFp.Category,
                                OwnerAccount = owner,
                                Description = matchedFp.Description,
                                Safety = matchedFp.Safety,
                                Recommendation = matchedFp.Recommendation
                            });
                            continue;
                        }

                        // 5. Installed Applications Registry Match (Tier 2)
                        if (_installedAppsCache.TryGetValue(dirName, out var appInfo))
                        {
                            results.Add(new FolderAttributionModel
                            {
                                Path = fullPath,
                                FolderName = dirName,
                                TotalSizeBytes = size,
                                OwningApplication = appInfo.DisplayName,
                                Publisher = appInfo.Publisher,
                                Category = "Installed Application Data",
                                OwnerAccount = owner,
                                Description = $"Storage data associated with installed software: {appInfo.DisplayName}",
                                Safety = CleanupSafety.CautionUserData,
                                Recommendation = "Examine sub-caches before deleting. Contains active application state or user profiles."
                            });
                            continue;
                        }

                        // 6. Orphaned App Detection (Requirement 1: Not in Registry, Not Running, Not System)
                        if (!SystemFolderWhitelist.Contains(dirName) && !dirName.StartsWith("."))
                        {
                            results.Add(new FolderAttributionModel
                            {
                                Path = fullPath,
                                FolderName = dirName,
                                TotalSizeBytes = size,
                                OwningApplication = $"{dirName} (Uninstalled)",
                                Publisher = "Uninstalled / Removed Software",
                                Category = "Orphaned App Leftover",
                                OwnerAccount = owner,
                                Description = $"Leftover application folder from software no longer registered in Windows.",
                                Safety = CleanupSafety.OrphanedLeftover,
                                Recommendation = "Application is no longer found in Windows Registry. Safe to remove leftover directory to reclaim space."
                            });
                            continue;
                        }

                        // 7. Heuristic Fallback for System / Generic Folders
                        string category = "System / User Data";
                        var safety = CleanupSafety.CautionUserData;
                        string rec = "Inspect folder contents before removal.";
                        string appName = dirName;
                        string publisher = "Windows / Third-Party Component";

                        if (dirName.Equals("Temp", StringComparison.OrdinalIgnoreCase) || dirName.Contains("Cache", StringComparison.OrdinalIgnoreCase))
                        {
                            category = "Cache / Temp";
                            safety = CleanupSafety.SafeToCleanCache;
                            rec = "Safe to clean. Temporary cache files.";
                        }
                        else if (dirName.Equals("Logs", StringComparison.OrdinalIgnoreCase) || dirName.Contains("CrashDumps", StringComparison.OrdinalIgnoreCase))
                        {
                            category = "Diagnostic Logs";
                            safety = CleanupSafety.SafeToDelete;
                            rec = "Safe to delete. Error reports and trace logs.";
                        }
                        else if (SystemFolderWhitelist.Contains(dirName))
                        {
                            safety = CleanupSafety.ProtectedSystem;
                            rec = "Core Windows component. Do not delete.";
                        }

                        results.Add(new FolderAttributionModel
                        {
                            Path = fullPath,
                            FolderName = dirName,
                            TotalSizeBytes = size,
                            OwningApplication = appName,
                            Publisher = publisher,
                            Category = category,
                            OwnerAccount = owner,
                            Description = $"Directory created by {appName} on your system.",
                            Safety = safety,
                            Recommendation = rec
                        });
                    }
                    catch { }
                }
            }
            catch { }

            return results;
        }

        private static List<(int Id, string ProcessName, string? ExePath)> GetRunningProcessesSnapshot()
        {
            var list = new List<(int Id, string ProcessName, string? ExePath)>();
            try
            {
                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        string? exePath = null;
                        try
                        {
                            exePath = proc.MainModule?.FileName;
                        }
                        catch { }

                        list.Add((proc.Id, proc.ProcessName, exePath));
                    }
                    catch { }
                }
            }
            catch { }
            return list;
        }

        private static (int Id, string ProcessName, string? ExePath)? FindActiveRunningProcessForFolder(
            string folderPath, string folderName, List<(int Id, string ProcessName, string? ExePath)> runningProcesses)
        {
            string cleanFolderPath = folderPath.TrimEnd('\\', '/');

            foreach (var proc in runningProcesses)
            {
                // A. Check if the process executable is running directly from inside this folder or subfolder
                if (!string.IsNullOrEmpty(proc.ExePath) &&
                    proc.ExePath.StartsWith(cleanFolderPath, StringComparison.OrdinalIgnoreCase))
                {
                    return proc;
                }

                // B. Check if process name exactly matches folder name (e.g. "discord" matches "Discord", "steam" matches "Steam")
                if (string.Equals(proc.ProcessName, folderName, StringComparison.OrdinalIgnoreCase))
                {
                    return proc;
                }

                // C. Check if folder contains process name or vice versa (if long enough)
                if (folderName.Length >= 4 &&
                    (proc.ProcessName.IndexOf(folderName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     folderName.IndexOf(proc.ProcessName, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return proc;
                }
            }

            return null;
        }

        private static (long TotalSize, int SampledFilesCount, int JunkFilesCount, double JunkRatio) AnalyzeFolderContentSafe(
            DirectoryInfo rootDir, int maxSampleFiles = 150)
        {
            long totalSize = 0;
            int sampledFilesCount = 0;
            int junkFilesCount = 0;

            var dirQueue = new Queue<string>();
            dirQueue.Enqueue(rootDir.FullName);

            int visitedDirs = 0;

            while (dirQueue.Count > 0 && visitedDirs < 30)
            {
                string currentDir = dirQueue.Dequeue();
                visitedDirs++;

                string[]? files = null;
                try
                {
                    files = Directory.GetFiles(currentDir);
                }
                catch { }

                if (files != null)
                {
                    foreach (var file in files)
                    {
                        long len = 0;
                        try
                        {
                            var fi = new FileInfo(file);
                            len = fi.Length;
                        }
                        catch { }

                        totalSize += len;

                        if (sampledFilesCount < maxSampleFiles)
                        {
                            sampledFilesCount++;
                            string ext = Path.GetExtension(file);
                            if (!string.IsNullOrEmpty(ext) && JunkExtensions.Contains(ext))
                            {
                                junkFilesCount++;
                            }
                        }
                    }
                }

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
                        dirQueue.Enqueue(sub);
                    }
                }
            }

            double ratio = sampledFilesCount > 0 ? (double)junkFilesCount / sampledFilesCount : 0.0;
            return (totalSize, sampledFilesCount, junkFilesCount, ratio);
        }

        private string GetDirectoryOwnerSafe(string directoryPath)
        {
            try
            {
                var dirInfo = new DirectoryInfo(directoryPath);
                var sec = dirInfo.GetAccessControl(AccessControlSections.Owner);
                var identity = sec.GetOwner(typeof(NTAccount));
                return identity?.Value ?? "SYSTEM";
            }
            catch
            {
                return Environment.UserName;
            }
        }
    }
}
