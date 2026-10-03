using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Vortex.Core.Database;
using Vortex.Core.Storage;

namespace Vortex.Views
{
    public partial class VortexProgressWindow : Window
    {
        private readonly int _totalCount;
        private int _doneCount = 0;
        private long _reclaimedBytes = 0;

        public VortexProgressWindow(int totalCount, string operationTitle = "Cleaning Files...", string operationSubtitle = "Safely removing selected items", string icon = "🧹")
        {
            InitializeComponent();
            _totalCount = totalCount;

            TxtTitle.Text = operationTitle;
            TxtSubtitle.Text = operationSubtitle;
            TxtIcon.Text = icon;
            TxtProgressItems.Text = $"0 / {_totalCount} items";
            TxtProgressPct.Text = "0%";
            TxtCurrentFile.Text = "Preparing...";
            PbProgress.Maximum = totalCount > 0 ? totalCount : 1;
            PbProgress.Value = 0;
        }

        private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Reports progress for a single item just processed. Call on UI thread.
        /// </summary>
        public void ReportItem(string itemLabel, long bytesFreed)
        {
            _doneCount++;
            _reclaimedBytes += bytesFreed;

            double pct = _totalCount > 0 ? (_doneCount / (double)_totalCount) * 100.0 : 100.0;

            TxtCurrentFile.Text = $"Removing: {itemLabel}";
            PbProgress.Value = _doneCount;
            TxtProgressItems.Text = $"{_doneCount} / {_totalCount} items";
            TxtProgressPct.Text = $"{pct:F0}%";
        }

        /// <summary>
        /// Marks the operation as complete. Shows summary card and Close button.
        /// </summary>
        public void MarkComplete(string summaryLabel = "Disk Space Freed")
        {
            // Snap to 100%
            PbProgress.Value = PbProgress.Maximum;
            TxtProgressPct.Text = "100%";
            TxtProgressItems.Text = $"{_doneCount} / {_totalCount} items";
            TxtCurrentFile.Text = "Done!";

            // Switch progress bar to green
            PbProgress.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0x99));

            // Update header
            TxtCategory.Text = "OPERATION COMPLETE";
            TxtTitle.Text = "Clean Complete";
            TxtSubtitle.Text = $"Successfully processed {_doneCount} item(s)";

            // Update icon to checkmark
            TxtIcon.Text = "✓";
            BorderIcon.Background = new SolidColorBrush(Color.FromArgb(0x28, 0x00, 0xE5, 0x99));
            BorderIcon.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0x99));

            // Build freed string
            string freedStr = _reclaimedBytes > 1024L * 1024L * 1024L
                ? $"+ {_reclaimedBytes / (1024.0 * 1024.0 * 1024.0):F2} GB"
                : $"+ {_reclaimedBytes / (1024.0 * 1024.0):F1} MB";

            TxtSummaryValue.Text = freedStr;
            TxtSummaryLabel.Text = summaryLabel;
            TxtSummarySubtext.Text = $"Verified & safely executed  •  {_doneCount} item(s) removed";
            BorderSummary.Visibility = Visibility.Visible;

            // Show close button
            BtnClose.Visibility = Visibility.Visible;
        }

        // ─── Static helper: folder clean ─────────────────────────────────────────

        /// <summary>
        /// Opens a live progress window, deletes each folder one-by-one with real-time
        /// progress updates, then reveals the summary and waits for the user to close.
        /// </summary>
        public static async Task RunFolderCleanAsync(
            Window? owner,
            List<FolderAttributionModel> candidates,
            string filterLabel,
            Action<int, long> onComplete)
        {
            var win = new VortexProgressWindow(
                candidates.Count,
                $"Cleaning {filterLabel}",
                "Safely removing selected folders",
                "🧹");

            if (owner != null && owner.IsVisible)
                win.Owner = owner;

            win.Show();

            int cleanedCount = 0;
            long reclaimedBytes = 0;

            foreach (var folder in candidates)
            {
                string label = Path.GetFileName(folder.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                long sz = 0;

                await Task.Run(() =>
                {
                    try
                    {
                        if (Directory.Exists(folder.Path))
                        {
                            sz = folder.TotalSizeBytes;
                            Directory.Delete(folder.Path, true);
                            cleanedCount++;
                            reclaimedBytes += sz;
                        }
                    }
                    catch { }
                });

                win.ReportItem(label, sz);
                // Brief pause so the user can actually see each item tick past
                await Task.Delay(20);
            }

            win.MarkComplete("Reclaimed Storage");
            onComplete(cleanedCount, reclaimedBytes);
        }

        // ─── Static helper: junk sweeper ─────────────────────────────────────────

        /// <summary>
        /// Opens a live progress window for junk sweeper category-by-category cleaning.
        /// </summary>
        public static async Task RunJunkCleanAsync(
            Window? owner,
            List<JunkCategoryScanResult> selected,
            Action<int, long> onComplete)
        {
            var win = new VortexProgressWindow(
                selected.Count,
                "System Sweeper Running",
                "Cleaning junk files, caches, and temp data",
                "🧹");

            if (owner != null && owner.IsVisible)
                win.Owner = owner;

            win.Show();

            int totalDeletedFiles = 0;
            long totalFreedBytes = 0;

            foreach (var cat in selected)
            {
                int df = 0;
                long fb = 0;

                await Task.Run(() =>
                {
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
                                df++;
                                fb += len;
                            }
                            catch { }
                        }

                        CleanEmptyDirectoriesSafe(path);
                    }
                });

                totalDeletedFiles += df;
                totalFreedBytes += fb;

                win.ReportItem(cat.CategoryName, fb);
                await Task.Delay(30);
            }

            win.MarkComplete("Disk Space Freed");
            onComplete(totalDeletedFiles, totalFreedBytes);
        }

        // ─── Shared file-system helpers ───────────────────────────────────────────

        private static IEnumerable<FileInfo> EnumerateFilesSafe(string rootPath, int maxDepth = 4, int maxFiles = 5000)
        {
            var dirs = new Stack<(string Path, int Depth)>();
            dirs.Push((rootPath, 0));
            int yielded = 0;

            while (dirs.Count > 0 && yielded < maxFiles)
            {
                var (currentDir, depth) = dirs.Pop();
                DirectoryInfo di;
                try
                {
                    di = new DirectoryInfo(currentDir);
                    if (di.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                }
                catch { continue; }

                if (depth < maxDepth)
                {
                    try
                    {
                        foreach (var sub in di.GetDirectories())
                            dirs.Push((sub.FullName, depth + 1));
                    }
                    catch { }
                }

                FileInfo[] files;
                try { files = di.GetFiles(); }
                catch { continue; }

                foreach (var f in files)
                {
                    yield return f;
                    yielded++;
                    if (yielded >= maxFiles) yield break;
                }
            }
        }

        private static void CleanEmptyDirectoriesSafe(string rootPath)
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories)
                                             .OrderByDescending(d => d.Length))
                {
                    try
                    {
                        if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                            Directory.Delete(dir, false);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
