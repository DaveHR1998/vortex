using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Threading;
using Vortex.Core.Privacy;

namespace Vortex.Core.SystemManagement
{
    public class WingetProgressUpdate
    {
        public double? Percent { get; set; }
        public string? DownloadedText { get; set; } // e.g. "45.2 MB / 120 MB"
        public string StatusText { get; set; } = string.Empty;
        public string RawLine { get; set; } = string.Empty;
    }

    /// <summary>
    /// Wraps winget for software update discovery (selective, per-app).
    /// </summary>
    public class WingetService
    {
        // ─── Availability check ───────────────────────────────────────────────

        public bool IsWingetAvailable()
        {
            try
            {
                var psi = new ProcessStartInfo("winget", "--version")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                };
                using var p = Process.Start(psi);
                if (p == null) return false;
                p.WaitForExit(5000);
                return p.ExitCode == 0;
            }
            catch { return false; }
        }

        // ─── Discover available updates ───────────────────────────────────────

        /// <summary>
        /// Runs "winget upgrade --include-unknown" and parses the tabular output.
        /// </summary>
        public async Task<List<WingetPackage>> GetAvailableUpdatesAsync()
        {
            var output = await RunWingetAsync("upgrade --include-unknown");
            return ParseUpgradeTable(output);
        }

        // ─── Upgrade a single package ─────────────────────────────────────────

        // ─── Upgrade a single package ─────────────────────────────────────────

        public class PackageUpdateSession
        {
            public string PackageId { get; set; } = string.Empty;
            public long? ExpectedTotalBytes { get; set; }
            public string? DownloadUrl { get; set; }
        }

        /// <summary>
        /// Upgrades one package by ID and streams stdout lines and live download progress.
        /// Supports cancellation to terminate the process tree.
        /// Cleans up temporary installer files automatically after installation.
        /// </summary>
        public async Task<bool> UpgradePackageAsync(
            string packageId, 
            string? source,
            IProgress<string> outputProgress,
            IProgress<WingetProgressUpdate>? downloadProgress = null,
            CancellationToken cancellationToken = default)
        {
            // Kill any lingering orphan winget processes from earlier runs to release file locks
            KillLingeringWingetProcesses();

            // Ensure winget uses fast wininet downloader rather than stalling Delivery Optimization
            EnsureFastWininetDownloader();

            // Pre-clean any stale installer files or locks for this package
            CleanupPackageTemp(packageId);

            // If Chrome is currently running, inform the user so they know to close Chrome if file locks occur
            if (packageId.Contains("Chrome", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (Process.GetProcessesByName("chrome").Length > 0)
                    {
                        outputProgress.Report("  ⚠️ [NOTE] Google Chrome is currently open. If the installer encounters file locks, please close Chrome tabs.");
                    }
                }
                catch { }
            }

            var session = new PackageUpdateSession { PackageId = packageId };

            using var monitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var monitorTask = MonitorDownloadProgressAsync(session, outputProgress, downloadProgress, monitorCts.Token);

            string sourceArg = !string.IsNullOrWhiteSpace(source) && !string.Equals(source, "msstore", StringComparison.OrdinalIgnoreCase)
                ? $"--source \"{source}\""
                : "--source winget";

            var args = $"upgrade --id \"{packageId}\" {sourceArg} --accept-package-agreements --accept-source-agreements --disable-interactivity --force";
            bool success = false;
            try
            {
                success = await RunWingetStreamAsync(args, session, outputProgress, downloadProgress, cancellationToken);
            }
            finally
            {
                try
                {
                    monitorCts.Cancel();
                    await monitorTask;
                }
                catch { }

                // Post-clean: Ensure installer binaries are not left taking up disk space
                CleanupPackageTemp(packageId);
            }

            return success;
        }

        // ─── Parsing ──────────────────────────────────────────────────────────

        private static List<WingetPackage> ParseUpgradeTable(string raw)
        {
            var result = new List<WingetPackage>();
            if (string.IsNullOrWhiteSpace(raw)) return result;

            var lines = raw.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

            // Find the separator line (a continuous run of dashes)
            int sepIdx = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.Length > 10 && trimmed.All(c => c == '-' || c == ' '))
                {
                    sepIdx = i;
                    break;
                }
            }
            if (sepIdx < 1) return result;

            var header = lines[sepIdx - 1];

            // Strategy 1: Find column start positions directly from the header text
            int idIdx    = header.IndexOf("Id", StringComparison.OrdinalIgnoreCase);
            int verIdx   = header.IndexOf("Version", StringComparison.OrdinalIgnoreCase);
            int availIdx = header.IndexOf("Available", StringComparison.OrdinalIgnoreCase);
            int srcIdx   = header.IndexOf("Source", StringComparison.OrdinalIgnoreCase);

            if (idIdx > 0 && verIdx > idIdx && availIdx > verIdx)
            {
                for (int i = sepIdx + 1; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.Contains("upgrade", StringComparison.OrdinalIgnoreCase) && line.Contains("available", StringComparison.OrdinalIgnoreCase))
                        break;
                    if (line.Length <= idIdx) continue;

                    string name    = SafeSubstring(line, 0, idIdx).Trim();
                    string id      = SafeSubstring(line, idIdx, verIdx - idIdx).Trim();
                    string current = SafeSubstring(line, verIdx, availIdx - verIdx).Trim();
                    string latest  = srcIdx > availIdx && line.Length >= srcIdx
                        ? SafeSubstring(line, availIdx, srcIdx - availIdx).Trim()
                        : SafeSubstring(line, availIdx, line.Length - availIdx).Trim();
                    string source  = srcIdx > availIdx && line.Length >= srcIdx
                        ? SafeSubstring(line, srcIdx, line.Length - srcIdx).Trim()
                        : "winget";

                    if (string.IsNullOrWhiteSpace(id)) continue;

                    bool isUnknown = current.Contains("?") || latest.Contains("?") ||
                                     string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(latest);

                    result.Add(new WingetPackage
                    {
                        Name       = name,
                        Id         = id,
                        CurrentVer = current,
                        LatestVer  = latest,
                        Source     = string.IsNullOrWhiteSpace(source) ? "winget" : source,
                        IsUnknown  = isUnknown,
                        IsSelected = !isUnknown
                    });
                }
                return result;
            }

            // Strategy 2: Fallback to separator runs (when separator has spaces between columns)
            var sep = lines[sepIdx];
            var colStarts = new List<int>();
            bool inDash = false;
            for (int i = 0; i < sep.Length; i++)
            {
                if (sep[i] == '-' && !inDash) { colStarts.Add(i); inDash = true; }
                else if (sep[i] == ' ')        inDash = false;
            }

            if (colStarts.Count >= 4)
            {
                for (int i = sepIdx + 1; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.Contains("upgrade", StringComparison.OrdinalIgnoreCase) && line.Contains("available", StringComparison.OrdinalIgnoreCase))
                        break;
                    if (line.Length < colStarts[2]) continue;

                    string Extract(int col) =>
                        col < colStarts.Count
                            ? (col + 1 < colStarts.Count
                                ? SafeSubstring(line, colStarts[col], colStarts[col + 1] - colStarts[col]).Trim()
                                : SafeSubstring(line, colStarts[col], line.Length - colStarts[col]).Trim())
                            : string.Empty;

                    var name    = Extract(0);
                    var id      = Extract(1);
                    var current = Extract(2);
                    var latest  = Extract(3);
                    var source  = colStarts.Count > 4 ? Extract(4) : "winget";

                    if (string.IsNullOrWhiteSpace(id)) continue;

                    bool isUnknown = current.Contains("?") || latest.Contains("?") ||
                                     string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(latest);

                    result.Add(new WingetPackage
                    {
                        Name       = name,
                        Id         = id,
                        CurrentVer = current,
                        LatestVer  = latest,
                        Source     = string.IsNullOrWhiteSpace(source) ? "winget" : source,
                        IsUnknown  = isUnknown,
                        IsSelected = !isUnknown
                    });
                }
            }

            return result;
        }

        // ─── Process runners ──────────────────────────────────────────────────

        private static async Task<string> RunWingetAsync(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("winget", args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };
                using var p = Process.Start(psi);
                if (p == null) return string.Empty;
                string stdout = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
                return stdout;
            }
            catch { return string.Empty; }
        }

        // ─── Downloader Configuration & Cache Management ─────────────────────

        /// <summary>
        /// Kills any orphaned winget.exe processes from earlier interrupted runs
        /// so they do not hold exclusive locks on downloaded installer binaries in Temp.
        /// </summary>
        public static void KillLingeringWingetProcesses()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("winget"))
                {
                    try { p.Kill(entireProcessTree: true); } catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Guarantees that winget is configured to use the high-performance 'wininet' downloader
        /// instead of 'dosvc' (Delivery Optimization), which frequently queues or deadlocks third-party downloads.
        /// </summary>
        public static void EnsureFastWininetDownloader()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string pkgFolder = Path.Combine(localAppData, "Packages", "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe", "LocalState");
                if (!Directory.Exists(pkgFolder)) Directory.CreateDirectory(pkgFolder);
                string settingsPath = Path.Combine(pkgFolder, "settings.json");

                bool needsWrite = true;
                if (File.Exists(settingsPath))
                {
                    string existing = File.ReadAllText(settingsPath);
                    if (existing.Contains("\"downloader\"", StringComparison.OrdinalIgnoreCase) &&
                        existing.Contains("\"wininet\"", StringComparison.OrdinalIgnoreCase))
                    {
                        needsWrite = false;
                    }
                }

                if (needsWrite)
                {
                    string json = "{\n  \"$schema\": \"https://aka.ms/winget-settings.schema.json\",\n  \"network\": {\n    \"downloader\": \"wininet\"\n  }\n}";
                    File.WriteAllText(settingsPath, json);
                }
            }
            catch { }
        }

        public static IEnumerable<string> GetWingetTempDirectories()
        {
            var dirs = new List<string>();
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string localWinGet = Path.Combine(localAppData, "Temp", "WinGet");
                if (Directory.Exists(localWinGet)) dirs.Add(localWinGet);
            }
            catch { }

            try
            {
                string sysTemp = Path.Combine(Path.GetTempPath(), "WinGet");
                if (Directory.Exists(sysTemp) && !dirs.Contains(sysTemp, StringComparer.OrdinalIgnoreCase))
                    dirs.Add(sysTemp);
            }
            catch { }

            return dirs;
        }

        /// <summary>
        /// Calculates total size of downloaded installer files in WinGet temp folders.
        /// </summary>
        public static long GetDownloadCacheSizeBytes()
        {
            long total = 0;
            foreach (var dir in GetWingetTempDirectories())
            {
                try
                {
                    var di = new DirectoryInfo(dir);
                    foreach (var file in di.EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        if (!file.FullName.Contains(@"\cache\", StringComparison.OrdinalIgnoreCase))
                        {
                            total += file.Length;
                        }
                    }
                }
                catch { }
            }
            return total;
        }

        /// <summary>
        /// Cleans all temporary installer files and folders in WinGet temp directories.
        /// Preserves the package database index folder.
        /// </summary>
        public static (long bytesFreed, int filesDeleted) ClearDownloadCache()
        {
            long bytesFreed = 0;
            int filesDeleted = 0;

            foreach (var dir in GetWingetTempDirectories())
            {
                try
                {
                    var di = new DirectoryInfo(dir);
                    foreach (var subDir in di.GetDirectories())
                    {
                        if (string.Equals(subDir.Name, "cache", StringComparison.OrdinalIgnoreCase))
                            continue; // Preserve winget index directory

                        try
                        {
                            foreach (var f in subDir.GetFiles("*", SearchOption.AllDirectories))
                            {
                                bytesFreed += f.Length;
                                filesDeleted++;
                            }
                            subDir.Delete(recursive: true);
                        }
                        catch { }
                    }

                    foreach (var file in di.GetFiles())
                    {
                        try
                        {
                            bytesFreed += file.Length;
                            file.Delete();
                            filesDeleted++;
                        }
                        catch { }
                    }
                }
                catch { }
            }

            return (bytesFreed, filesDeleted);
        }

        /// <summary>
        /// Cleans up any cached installer folders or locks specific to a package ID.
        /// </summary>
        public static void CleanupPackageTemp(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId)) return;

            foreach (var dir in GetWingetTempDirectories())
            {
                try
                {
                    var di = new DirectoryInfo(dir);
                    foreach (var sub in di.GetDirectories($"*{packageId}*"))
                    {
                        try { sub.Delete(recursive: true); } catch { }
                    }
                    foreach (var file in di.GetFiles($"*{packageId}*"))
                    {
                        try { file.Delete(); } catch { }
                    }
                }
                catch { }
            }
        }

        // ─── Real-Time Download File Poller ───────────────────────────────────

        // ─── Real-Time Download File Poller ───────────────────────────────────

        private static async Task MonitorDownloadProgressAsync(
            PackageUpdateSession session,
            IProgress<string> outputProgress,
            IProgress<WingetProgressUpdate>? downloadProgress, 
            CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            long lastBytes = 0;
            double lastSpeedSec = 0;
            long lastSpeedBytes = 0;
            double speedBytesPerSec = 0;
            DateTime lastLogTime = DateTime.MinValue;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(300, ct);

                    long currentBytes = 0;

                    foreach (var dir in GetWingetTempDirectories())
                    {
                        if (!Directory.Exists(dir)) continue;

                        var di = new DirectoryInfo(dir);
                        DirectoryInfo[] subDirs;
                        try { subDirs = di.GetDirectories(); } catch { continue; }

                        foreach (var sub in subDirs)
                        {
                            if (string.Equals(sub.Name, "cache", StringComparison.OrdinalIgnoreCase)) continue;

                            bool match = (!string.IsNullOrEmpty(session.PackageId) && 
                                          (sub.Name.Contains(session.PackageId, StringComparison.OrdinalIgnoreCase) || 
                                           session.PackageId.Contains(sub.Name, StringComparison.OrdinalIgnoreCase)))
                                         || (DateTime.UtcNow - sub.LastWriteTimeUtc).TotalMinutes < 15;

                            if (match)
                            {
                                FileInfo[] files;
                                try { files = sub.GetFiles(); } catch { continue; }

                                foreach (var f in files)
                                {
                                    try
                                    {
                                        using var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                                        currentBytes += fs.Length;
                                    }
                                    catch
                                    {
                                        try { f.Refresh(); currentBytes += f.Length; } catch { }
                                    }
                                }
                            }
                        }
                    }

                    if (currentBytes > 0)
                    {
                        double nowSec = sw.Elapsed.TotalSeconds;
                        double dt = nowSec - lastSpeedSec;
                        if (dt >= 0.5)
                        {
                            speedBytesPerSec = Math.Max(0, (currentBytes - lastSpeedBytes) / dt);
                            lastSpeedBytes = currentBytes;
                            lastSpeedSec = nowSec;
                        }

                        double dlMb = currentBytes / (1024.0 * 1024.0);
                        double speedMb = speedBytesPerSec / (1024.0 * 1024.0);
                        string speedText = speedMb >= 1.0 ? $"{speedMb:F1} MB/s" : $"{speedBytesPerSec / 1024.0:F0} KB/s";

                        double? pct = null;
                        string detailText;

                        if (session.ExpectedTotalBytes.HasValue && session.ExpectedTotalBytes.Value > 0)
                        {
                            double totalMb = session.ExpectedTotalBytes.Value / (1024.0 * 1024.0);
                            pct = Math.Clamp(((double)currentBytes / session.ExpectedTotalBytes.Value) * 100.0, 0, 99.5);
                            detailText = $"{dlMb:F1} MB / {totalMb:F1} MB ({pct.Value:F0}%) • {speedText}";
                        }
                        else
                        {
                            detailText = $"{dlMb:F1} MB downloaded • {speedText}";
                        }

                        downloadProgress?.Report(new WingetProgressUpdate
                        {
                            Percent = pct,
                            DownloadedText = detailText,
                            StatusText = "Downloading package..."
                        });

                        // Report to terminal every 2.5 seconds when bytes change so user sees life in the console
                        if ((DateTime.UtcNow - lastLogTime).TotalSeconds >= 2.5 && currentBytes != lastBytes)
                        {
                            lastLogTime = DateTime.UtcNow;
                            outputProgress.Report($"  ⤓ {detailText}");
                        }

                        lastBytes = currentBytes;
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }

        // ─── Stream Process Execution ─────────────────────────────────────────

        private static readonly Regex UrlRegex = new Regex(@"Downloading\s+(https?://\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SizeRegex = new Regex(@"(\d+(?:\.\d+)?\s*(?:B|KB|MB|GB))\s*/\s*(\d+(?:\.\d+)?\s*(?:B|KB|MB|GB))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PercentRegex = new Regex(@"(\d{1,3}(?:\.\d+)?)\s*%", RegexOptions.Compiled);
        private static readonly Regex AnsiRegex = new Regex(@"\x1B\[[^@-~]*[@-~]", RegexOptions.Compiled);

        private static async Task<bool> RunWingetStreamAsync(
            string args, 
            PackageUpdateSession session,
            IProgress<string> progress,
            IProgress<WingetProgressUpdate>? downloadProgress,
            CancellationToken ct)
        {
            Process? p = null;
            try
            {
                var psi = new ProcessStartInfo("winget", args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding  = Encoding.UTF8
                };
                p = Process.Start(psi);
                if (p == null) { progress.Report("[ERROR] Could not start winget."); return false; }

                // Register cancellation to immediately kill winget and its child installer processes
                using var registration = ct.Register(() =>
                {
                    try
                    {
                        if (!p.HasExited)
                        {
                            p.Kill(entireProcessTree: true);
                        }
                    }
                    catch { }
                });

                // Consume stdout and stderr in parallel tasks to eliminate pipe buffer deadlocks
                var stdoutTask = Task.Run(async () =>
                {
                    var reader = p.StandardOutput;
                    char[] buffer = new char[512];
                    var sb = new StringBuilder();

                    while (!ct.IsCancellationRequested)
                    {
                        int read = await reader.ReadAsync(buffer, 0, buffer.Length);
                        if (read == 0) break; // EOF

                        for (int i = 0; i < read; i++)
                        {
                            char c = buffer[i];
                            if (c == '\r' || c == '\n')
                            {
                                if (sb.Length > 0)
                                {
                                    string rawLine = sb.ToString();
                                    sb.Clear();
                                    HandleWingetStreamLine(rawLine, session, progress, downloadProgress);
                                }
                            }
                            else
                            {
                                sb.Append(c);
                            }
                        }
                    }

                    if (sb.Length > 0)
                    {
                        HandleWingetStreamLine(sb.ToString(), session, progress, downloadProgress);
                    }
                }, ct);

                var stderrTask = Task.Run(async () =>
                {
                    var reader = p.StandardError;
                    char[] buffer = new char[512];
                    var sb = new StringBuilder();

                    while (!ct.IsCancellationRequested)
                    {
                        int read = await reader.ReadAsync(buffer, 0, buffer.Length);
                        if (read == 0) break; // EOF

                        for (int i = 0; i < read; i++)
                        {
                            char c = buffer[i];
                            if (c == '\r' || c == '\n')
                            {
                                if (sb.Length > 0)
                                {
                                    string rawLine = sb.ToString();
                                    sb.Clear();
                                    HandleWingetStreamLine(rawLine, session, progress, downloadProgress);
                                }
                            }
                            else
                            {
                                sb.Append(c);
                            }
                        }
                    }

                    if (sb.Length > 0)
                    {
                        HandleWingetStreamLine(sb.ToString(), session, progress, downloadProgress);
                    }
                }, ct);

                await Task.WhenAll(stdoutTask, stderrTask);
                await p.WaitForExitAsync(ct);
                return p.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                progress.Report("⏹ [CANCELLED] Update operation cancelled by user.");
                return false;
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested)
                {
                    progress.Report("⏹ [CANCELLED] Update operation cancelled by user.");
                    return false;
                }
                progress.Report($"[ERROR] {ex.Message}");
                return false;
            }
            finally
            {
                try
                {
                    if (p != null && !p.HasExited)
                    {
                        p.Kill(entireProcessTree: true);
                    }
                    p?.Dispose();
                }
                catch { }
            }
        }

        private static void HandleWingetStreamLine(
            string line, 
            PackageUpdateSession session,
            IProgress<string> textProgress, 
            IProgress<WingetProgressUpdate>? downloadProgress)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            string clean = AnsiRegex.Replace(line, "").Trim();
            if (string.IsNullOrWhiteSpace(clean)) return;

            // Stream to console / terminal
            textProgress.Report(clean);

            // Proactively detect package download URL and probe total package size in background
            var urlMatch = UrlRegex.Match(clean);
            if (urlMatch.Success)
            {
                string url = urlMatch.Groups[1].Value;
                session.DownloadUrl = url;
                Task.Run(async () =>
                {
                    try
                    {
                        using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                        using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, url);
                        using var res = await client.SendAsync(req, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                        if (res.IsSuccessStatusCode && res.Content.Headers.ContentLength.HasValue)
                        {
                            long len = res.Content.Headers.ContentLength.Value;
                            session.ExpectedTotalBytes = len;
                            double mb = len / (1024.0 * 1024.0);
                            textProgress.Report($"  📦 Total package size: {mb:F1} MB");
                        }
                    }
                    catch { }
                });
            }

            if (downloadProgress == null) return;

            var update = new WingetProgressUpdate { RawLine = clean };

            // Check for download size pattern e.g. "12.5 MB / 50.0 MB"
            var sizeMatch = SizeRegex.Match(clean);
            if (sizeMatch.Success)
            {
                update.DownloadedText = $"{sizeMatch.Groups[1].Value} / {sizeMatch.Groups[2].Value}";
            }

            // Check for percentage pattern e.g. "45%"
            var pctMatch = PercentRegex.Match(clean);
            if (pctMatch.Success && double.TryParse(pctMatch.Groups[1].Value, out double pct))
            {
                update.Percent = Math.Clamp(pct, 0, 100);
            }

            // Determine status text
            if (clean.Contains("Downloading", StringComparison.OrdinalIgnoreCase))
            {
                update.StatusText = "Downloading package...";
            }
            else if (clean.Contains("Verifying", StringComparison.OrdinalIgnoreCase) || clean.Contains("hash", StringComparison.OrdinalIgnoreCase))
            {
                update.StatusText = "Verifying installer hash & integrity...";
                update.DownloadedText = "Hash verified";
            }
            else if (clean.Contains("Starting package install", StringComparison.OrdinalIgnoreCase) || clean.Contains("Installing", StringComparison.OrdinalIgnoreCase))
            {
                update.StatusText = "Running package installer...";
                update.DownloadedText = "Installing package (please wait)...";
                update.Percent = 100;
            }
            else if (clean.Contains("Successfully installed", StringComparison.OrdinalIgnoreCase))
            {
                update.StatusText = "Installed successfully!";
                update.DownloadedText = "Complete";
                update.Percent = 100;
            }

            downloadProgress.Report(update);
        }

        private static string SafeSubstring(string s, int start, int length)
        {
            if (start >= s.Length) return string.Empty;
            int end = Math.Min(start + length, s.Length);
            return s[start..end];
        }
    }
}
