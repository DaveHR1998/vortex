using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Vortex.Core.Database;
using Vortex.Core.Modes;
using Vortex.Core.Privacy;
using Vortex.Core.ProcessManagement;
using Vortex.Core.Storage;
using Vortex.Core.SystemInfo;
using Vortex.Core.SystemIntegration;
using Vortex.Core.SystemManagement;
using Vortex.Core.Vault;
using Vortex.Views;

namespace Vortex
{
    public partial class MainWindow : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33; // 2 = Round
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;      // 2 = Mica, 3 = Acrylic, 4 = Mica Alt

        private readonly DatabaseService _db;
        private readonly NetworkMonitor _netMonitor;
        private readonly MemoryOptimizer _memOptimizer;
        private readonly ProcessManager _procManager;
        private readonly FolderProvenanceEngine _provenanceEngine;
        private readonly SystemCleaner _systemCleaner;
        private readonly RegistryGuard _regGuard;
        private readonly ServiceControllerService _svcController;
        private readonly HardwareMonitor _hardwareMonitor;
        private readonly FocusModeManager _focusManager;
        private readonly PowerAndIdleManager _powerManager;
        private readonly ThemeService _themeService;
        private readonly VaultCryptoEngine _vaultCrypto;
        private readonly UsbDlpService _usbDlp;
        private readonly FolderLockService _folderLockService;
        private TrayIconService? _trayService;

        private VortexOrbWindow? _orbWindow;
        private readonly DispatcherTimer _telemetryTimer;
        private readonly PrivacyEraser _privacyEraser;
        private readonly IdentityScanner _identityScanner;
        private readonly WingetService _wingetService;
        private readonly ProcessBlockService _blockService;
        private DispatcherTimer? _sensorTimer;
        private DispatcherTimer? _netAutoRefreshTimer;
        private bool _isNetAutoRefreshRunning = false;

        // Cached spec data for System Info tab
        private List<HardwareSpecItem>? _specsCpu, _specsGpu, _specsMem, _specsStorage, _specsMb, _specsOs;

        private List<ProcessInfoModel> _allProcesses = new();
        private List<FolderAttributionModel> _allFolders = new();
        private List<JunkCategoryScanResult> _currentJunk = new();
        private List<RegistryIssueModel> _currentRegIssues = new();
        private List<NetworkMonitor.NetworkConnectionInfo> _allNetworkConns = new();
        private List<DiscoveredAppModel> _allNetDiscoveredApps = new();
        private List<AppAccessRule> _allNetAllowedApps = new();
        private List<ProcessBlockService.BlockedItemModel> _allNetBlockedApps = new();
        private List<VaultFolderModel> _allVaultFolders = new();
        private List<RemovableDriveModel> _allRemovableDrives = new();

        public MainWindow()
        {
            InitializeComponent();
            LoadWindowIconSafely();

            _db = new DatabaseService();
            _blockService = new ProcessBlockService(_db);
            _netMonitor = new NetworkMonitor(_db);
            _memOptimizer = new MemoryOptimizer(_db);
            _procManager = new ProcessManager(_db, _netMonitor);
            _provenanceEngine = new FolderProvenanceEngine(_db);
            _systemCleaner = new SystemCleaner(_db);
            _regGuard = new RegistryGuard(_db);
            _svcController = new ServiceControllerService();
            _hardwareMonitor = new HardwareMonitor();
            _focusManager = new FocusModeManager(_db);
            _powerManager = new PowerAndIdleManager();
            _themeService = new ThemeService();
            _privacyEraser = new PrivacyEraser();
            _identityScanner = new IdentityScanner();
            _wingetService = new WingetService();
            _vaultCrypto = new VaultCryptoEngine();
            _usbDlp = new UsbDlpService(_db);
            _folderLockService = new FolderLockService(_db, _vaultCrypto, _usbDlp);

            _folderLockService.OnVaultStateChanged += () => Dispatcher.Invoke(RefreshVaultView);
            _folderLockService.OnLockedFolderAccessed += (vault) => Dispatcher.Invoke(() => OnLockedFolderAttemptDetected(vault));
            _folderLockService.OnAutoLockTriggered += (reason) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _trayService?.ShowNotification("🔒 Vortex Vault Locked", $"All folders sealed: {reason}");
                    TxtStatusBar.Text = $"🔒 Vault Auto-Locked: {reason}";
                });
            };

            _usbDlp.OnUsbWriteProtectChanged += (isProtected) => Dispatcher.Invoke(RefreshVaultView);
            _usbDlp.OnTemporaryWindowTick += (remaining) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (TxtUsbCountdown != null)
                    {
                        TxtUsbCountdown.Text = $"⏳ Temporary Write Window Active: {remaining:mm\\:ss} remaining before auto-lock.";
                    }
                });
            };
            _usbDlp.OnTemporaryWindowExpired += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    _trayService?.ShowNotification("🔒 USB Write Protection Re-engaged", "Temporary write window expired. Removable drives are now Read-Only.");
                    TxtStatusBar.Text = "🔒 USB Write-Protection re-engaged (Read-Only).";
                    RefreshVaultView();
                });
            };

            _trayService = new TrayIconService(
                ownerWindow: this,
                openDashboard: () => Dispatcher.Invoke(RestoreMainWindow),
                turboBoost: () => Dispatcher.Invoke(() => OnTurboBoostClick(this, new RoutedEventArgs())),
                toggleOrb: () => Dispatcher.Invoke(() => OnToggleOrbClick(this, new RoutedEventArgs())),
                toggleFocus: () => Dispatcher.Invoke(ToggleFocusMode),
                exitApp: () => Dispatcher.Invoke(ExitApplicationPermanently)
            );

            // Hook intrusion detection alerts from NetworkMonitor
            _netMonitor.OnThreatDetected += (conn, reason) =>
            {
                if (_blockService.IsAppAllowed(conn.ProcessName, conn.ProcessPath) || _blockService.IsNotificationMuted(conn.ProcessName))
                    return;

                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        System.Media.SystemSounds.Exclamation.Play();
                        _trayService?.ShowBalloon(
                            "🚨 Vortex Intrusion Alert",
                            $"{conn.ProcessName} (PID {conn.ProcessId}): {reason}",
                            2);
                        TxtStatusBar.Text = $"🚨 THREAT DETECTED: {conn.ProcessName} (PID {conn.ProcessId}) - {reason}";
                    }
                    catch { }
                });
            };

            _netAutoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _netAutoRefreshTimer.Tick += (s, e) =>
            {
                if (NavNetwork.IsChecked == true)
                {
                    RefreshNetworkSockets();
                }
            };

            SourceInitialized += OnSourceInitialized;

            _telemetryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _telemetryTimer.Tick += (s, e) => UpdateHeaderTelemetry();
            _telemetryTimer.Start();

            Loaded += async (s, e) =>
            {
                try
                {
                    UpdateHeaderTelemetry();
                    await RefreshProcessesAsync();
                    ShowFloatingOrb();
                }
                catch { }
            };
        }

        private void RestoreMainWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void ToggleFocusMode()
        {
            if (_focusManager.IsFocusModeActive)
            {
                _focusManager.DisableFocusMode();
                _trayService?.ShowNotification("Focus Mode Disabled", "Background developer and sync services resumed.");
            }
            else
            {
                _focusManager.EnableFocusMode();
                _trayService?.ShowNotification("Gaming / Focus Mode Active", "Background daemons suspended. System resources dedicated to foreground performance.");
            }
        }

        private void ExitApplicationPermanently()
        {
            _telemetryTimer?.Stop();
            _netAutoRefreshTimer?.Stop();
            _trayService?.Dispose();
            _orbWindow?.Close();
            System.Windows.Application.Current.Shutdown();
        }

        private void LoadWindowIconSafely()
        {
            try
            {
                string localFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vortex.png");
                if (System.IO.File.Exists(localFile))
                {
                    Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(localFile, UriKind.Absolute));
                }
                else
                {
                    var iconUri = new Uri("pack://application:,,,/vortex.png");
                    Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
                }
            }
            catch { }
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            UpdateDwmBackdrop(_themeService.IsDarkMode);
        }

        private void UpdateDwmBackdrop(bool isDark)
        {
            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                if (handle != IntPtr.Zero)
                {
                    int darkMode = isDark ? 1 : 0;
                    DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

                    int corners = 2; // DWMWCP_ROUND
                    DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));

                    int backdropType = 3; // Acrylic
                    DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
                }
            }
            catch
            {
                // Windows 10 fallback
            }
        }

        private void OnToggleThemeClick(object sender, RoutedEventArgs e)
        {
            _themeService.ToggleTheme();
            UpdateDwmBackdrop(_themeService.IsDarkMode);
            BtnToggleTheme.Content = _themeService.IsDarkMode ? "🌙 Dark" : "☀️ Light";
            TxtStatusBar.Text = $"Theme switched to {(_themeService.IsDarkMode ? "Proton Dark" : "Fluent Light")}.";
        }

        private void UpdateHeaderTelemetry()
        {
            try
            {
                var mem = _memOptimizer.GetMemoryStatus();
                double totalGb = mem.totalBytes / (1024.0 * 1024.0 * 1024.0);
                double usedGb = (mem.totalBytes - mem.availBytes) / (1024.0 * 1024.0 * 1024.0);
                
                TxtHeaderRam.Text = $"{mem.loadPercent}% ({usedGb:F1} / {totalGb:F0} GB)";
                TxtOverviewRamLoad.Text = $"{mem.loadPercent}% ({usedGb:F1} GB in use)";
                PbOverviewRam.Value = mem.loadPercent;

                float cpu = _hardwareMonitor.GetCpuUsagePercentage();
                TxtHeaderCpu.Text = $"{cpu:F0}%";
                TxtOverviewCpuLoad.Text = $"{cpu:F0}%";
                PbOverviewCpu.Value = cpu;

                var drive = _hardwareMonitor.GetDriveStorage("C");
                TxtHeaderDisk.Text = $"{drive.usedPercentage:F0}% Used";
            }
            catch { }
        }

        #region Window Controls
        private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void OnMaximizeClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Hide();
            _trayService?.ShowNotification("Vortex Active in Background", "Vortex is monitoring and keeping your memory clean. Click the tray icon to reopen.");
        }

        private void OnToggleOrbClick(object sender, RoutedEventArgs e)
        {
            if (_orbWindow == null || !_orbWindow.IsVisible)
            {
                ShowFloatingOrb();
            }
            else
            {
                _orbWindow.Hide();
                BtnToggleOrb.Content = "🔮 Show Desktop Orb";
            }
        }

        private void ShowFloatingOrb()
        {
            if (_orbWindow == null)
            {
                _orbWindow = new VortexOrbWindow(_memOptimizer, _focusManager, () =>
                {
                    Show();
                    WindowState = WindowState.Normal;
                    Activate();
                });
            }
            _orbWindow.Show();
            BtnToggleOrb.Content = "🔮 Hide Desktop Orb";
        }
        #endregion

        #region Navigation & Breadcrumb
        private void OnNavSelectionChanged(object sender, RoutedEventArgs e)
        {
            if (PanelHome == null) return;

            PanelHome.Visibility = NavHome.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelProcesses.Visibility = NavProcesses.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelStorage.Visibility = NavStorage.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelJunk.Visibility = NavJunk.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelRegistry.Visibility = NavRegistry.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelNetwork.Visibility = NavNetwork.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelServices.Visibility = NavServices.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelFocus.Visibility = NavFocus.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelHistory.Visibility = NavHistory.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelPrivacy.Visibility = NavPrivacy.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelIdentity.Visibility = NavIdentity.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelVault.Visibility = NavVault.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelUpdater.Visibility = NavUpdater.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelSysInfo.Visibility = NavSysInfo.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

            // Stop sensor timer when not on System Info tab
            if (NavSysInfo.IsChecked != true && _sensorTimer != null)
            {
                _sensorTimer.Stop();
                _sensorTimer = null;
            }

            // Pause network auto-refresh when navigating away from Network tab to conserve cycles
            if (NavNetwork.IsChecked != true && _isNetAutoRefreshRunning)
            {
                _netAutoRefreshTimer?.Stop();
                _isNetAutoRefreshRunning = false;
                BtnNetAutoRefresh.Content = "▶️ Auto-Refresh (2s)";
                BtnNetAutoRefresh.Style = (Style)FindResource("VortexSecondaryButton");
            }

            if (NavHome.IsChecked == true) TxtBreadcrumb.Text = "Overview & Health";
            else if (NavProcesses.IsChecked == true)
            {
                TxtBreadcrumb.Text = "Process Intelligence";
                if (_allProcesses.Count == 0) _ = RefreshProcessesAsync();
            }
            else if (NavStorage.IsChecked == true)
            {
                TxtBreadcrumb.Text = "Folder Ownership Explorer";
                if (LvFolders.ItemsSource == null)
                    _ = ScanStorageFolderAsync(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            }
            else if (NavJunk.IsChecked == true)
            {
                TxtBreadcrumb.Text = "System Sweeper";
                if (LvJunkCategories.ItemsSource == null) _ = ScanJunkAsync();
            }
            else if (NavRegistry.IsChecked == true)
            {
                TxtBreadcrumb.Text = "Registry Guard";
                if (LvRegistry.ItemsSource == null) _ = ScanRegistryAsync();
            }
            else if (NavNetwork.IsChecked == true)
            {
                TxtBreadcrumb.Text = "Process Network, Bandwidth & Threat Shield";
                RefreshNetworkSockets();
                if (!_isNetAutoRefreshRunning)
                {
                    _netAutoRefreshTimer?.Start();
                    _isNetAutoRefreshRunning = true;
                    BtnNetAutoRefresh.Content = "⏸️ Pause Auto-Refresh";
                    BtnNetAutoRefresh.Style = (Style)FindResource("VortexPrimaryButton");
                }
            }
            else if (NavServices.IsChecked == true)
            {
                TxtBreadcrumb.Text = "Services & Daemons";
                RefreshServicesList();
            }
            else if (NavFocus.IsChecked == true)
            {
                TxtBreadcrumb.Text = "Gaming & Focus Mode";
            }
            else if (NavHistory.IsChecked == true)
            {
                TxtBreadcrumb.Text = "Audit History & Logs";
                RefreshHistoryLogs();
            }
            else if (NavPrivacy.IsChecked == true)
            {
                TxtBreadcrumb.Text = "Privacy Eraser";
                if (LvPrivacy.ItemsSource == null) _ = ScanPrivacyTracesAsync();
            }
            else if (NavIdentity.IsChecked == true)
            {
                TxtBreadcrumb.Text = "Identity Shield";
                if (LvIdentity.ItemsSource == null) _ = ScanIdentityAsync();
            }
            else if (NavVault.IsChecked == true)
            {
                TxtBreadcrumb.Text = "Data Shield & Zero-Trust File Vault";
                RefreshVaultView();
            }
            else if (NavUpdater.IsChecked == true)
            {
                TxtBreadcrumb.Text = "Software Updater";
            }
            else if (NavSysInfo.IsChecked == true)
            {
                TxtBreadcrumb.Text = "System Information";
                _ = LoadSystemInfoAsync();
                StartSensorTimer();
            }
        }

        private void OnCardProcessesClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            NavProcesses.IsChecked = true;
        }

        private void OnCardStorageClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            NavStorage.IsChecked = true;
        }

        private void OnCardJunkClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            NavJunk.IsChecked = true;
        }

        private void OnSearchGotFocus(object sender, RoutedEventArgs e)
        {
        }

        private void OnSearchLostFocus(object sender, RoutedEventArgs e)
        {
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtSearchBoxPlaceholder != null)
            {
                TxtSearchBoxPlaceholder.Visibility = string.IsNullOrEmpty(TxtSearchBox?.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (TxtProcessFilter != null && TxtSearchBox != null && TxtProcessFilter.Text != TxtSearchBox.Text)
            {
                TxtProcessFilter.Text = TxtSearchBox.Text;
            }

            ApplyProcessFilter();
        }

        private void OnProcessFilterInputChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtProcessFilterPlaceholder != null)
            {
                TxtProcessFilterPlaceholder.Visibility = string.IsNullOrEmpty(TxtProcessFilter?.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (TxtSearchBox != null && TxtProcessFilter != null && TxtSearchBox.Text != TxtProcessFilter.Text)
            {
                TxtSearchBox.Text = TxtProcessFilter.Text;
            }

            ApplyProcessFilter();
        }
        #endregion

        #region TAB 1: Process & Memory Optimizer
        private void OnProcessFilterChanged(object sender, RoutedEventArgs e)
        {
            ApplyProcessFilter();
        }

        private void ApplyProcessFilter()
        {
            if (_allProcesses == null || _allProcesses.Count == 0) return;

            string query = (TxtSearchBox?.Text ?? TxtProcessFilter?.Text ?? string.Empty).Trim();

            IEnumerable<ProcessInfoModel> filtered = _allProcesses;

            if (RbFilterSafe?.IsChecked == true)
            {
                filtered = filtered.Where(p => p.SafetyLevel == ProcessSafetyLevel.SafeToTerminate);
            }
            else if (RbFilterDev?.IsChecked == true)
            {
                filtered = filtered.Where(p => p.SafetyLevel == ProcessSafetyLevel.DeveloperDaemon);
            }
            else if (RbFilterUser?.IsChecked == true)
            {
                filtered = filtered.Where(p => p.SafetyLevel == ProcessSafetyLevel.UserApplication);
            }
            else if (RbFilterCrit?.IsChecked == true)
            {
                filtered = filtered.Where(p => p.SafetyLevel == ProcessSafetyLevel.SystemCritical);
            }

            if (!string.IsNullOrEmpty(query))
            {
                filtered = filtered.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                              p.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                              p.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            LvProcesses.ItemsSource = filtered.ToList();
            UpdateHeaderSelectState();
            UpdateSelectionSummary();
        }

        private void OnSelectAllSafeClick(object sender, RoutedEventArgs e)
        {
            if (_allProcesses == null) return;
            foreach (var p in _allProcesses)
            {
                if (p.SafetyLevel == ProcessSafetyLevel.SafeToTerminate)
                {
                    p.IsSelected = true;
                }
            }
            UpdateHeaderSelectState();
            UpdateSelectionSummary();
        }

        private void OnClearSelectionClick(object sender, RoutedEventArgs e)
        {
            if (_allProcesses == null) return;
            foreach (var p in _allProcesses)
            {
                p.IsSelected = false;
            }
            if (ChkHeaderSelectAll != null) ChkHeaderSelectAll.IsChecked = false;
            UpdateSelectionSummary();
        }

        private void OnHeaderSelectAllClick(object sender, RoutedEventArgs e)
        {
            bool checkAll = ChkHeaderSelectAll.IsChecked == true;
            if (LvProcesses.ItemsSource is IEnumerable<ProcessInfoModel> visibleList)
            {
                foreach (var p in visibleList)
                {
                    p.IsSelected = checkAll;
                }
            }
            UpdateSelectionSummary();
        }

        private void OnProcessRowCheckboxClick(object sender, RoutedEventArgs e)
        {
            UpdateHeaderSelectState();
            UpdateSelectionSummary();
        }

        private void UpdateHeaderSelectState()
        {
            if (ChkHeaderSelectAll == null) return;
            if (LvProcesses.ItemsSource is IEnumerable<ProcessInfoModel> visibleList && visibleList.Any())
            {
                int total = visibleList.Count();
                int checkedCount = visibleList.Count(p => p.IsSelected);
                ChkHeaderSelectAll.IsChecked = checkedCount == total ? true : (checkedCount == 0 ? false : null);
            }
            else
            {
                ChkHeaderSelectAll.IsChecked = false;
            }
        }

        private void UpdateSelectionSummary()
        {
            var selected = _allProcesses.Where(p => p.IsSelected).ToList();
            if (selected.Count > 0)
            {
                long totalBytes = selected.Sum(p => p.MemoryBytes);
                string memStr = totalBytes > 1024L * 1024L * 1024L
                    ? $"{totalBytes / (1024.0 * 1024.0 * 1024.0):F2} GB"
                    : $"{totalBytes / (1024.0 * 1024.0):F1} MB";

                bool hasCritical = selected.Any(p => p.SafetyLevel == ProcessSafetyLevel.SystemCritical);
                int safeCount = selected.Count(p => p.SafetyLevel == ProcessSafetyLevel.SafeToTerminate);
                int devCount = selected.Count(p => p.SafetyLevel == ProcessSafetyLevel.DeveloperDaemon);
                int userCount = selected.Count(p => p.SafetyLevel == ProcessSafetyLevel.UserApplication);

                TxtSelectedProcName.Text = $"Selected: {selected.Count} Process(es) • Total RAM: {memStr}";

                if (hasCritical)
                {
                    TxtSelectedProcRec.Text = "⚠️ Protected System Critical component included. Termination is blocked by Windows safety protocol.";
                    BtnEndProcess.IsEnabled = false;
                }
                else
                {
                    TxtSelectedProcRec.Text = $"{safeCount} Safe to End, {devCount} Dev Service(s), {userCount} User App(s). Ready for termination.";
                    BtnEndProcess.IsEnabled = true;
                }

                BtnEndProcess.Content = selected.Count > 1 ? $"🛑 End Selected ({selected.Count})" : "🛑 End Process";
                BtnTrimProcess.Content = selected.Count > 1 ? $"⚡ Trim Selected ({selected.Count})" : "⚡ Trim Working Set";
                BtnTrimProcess.IsEnabled = true;
            }
            else if (LvProcesses.SelectedItem is ProcessInfoModel proc)
            {
                TxtSelectedProcName.Text = $"{proc.DisplayName} ({proc.Name}.exe - PID {proc.Id}) • {proc.MemoryString}";
                TxtSelectedProcRec.Text = $"{proc.Recommendation} • Publisher: {proc.Publisher}";
                BtnEndProcess.IsEnabled = proc.CanTerminate;
                BtnEndProcess.Content = "🛑 End Process";
                BtnTrimProcess.Content = "⚡ Trim Working Set";
                BtnTrimProcess.IsEnabled = true;
            }
            else
            {
                TxtSelectedProcName.Text = "Select one or more processes above to inspect recommendations";
                TxtSelectedProcRec.Text = "Vortex heuristics will display whether it is safe to terminate.";
                BtnEndProcess.IsEnabled = false;
                BtnEndProcess.Content = "🛑 End Process";
                BtnTrimProcess.Content = "⚡ Trim Working Set";
                BtnTrimProcess.IsEnabled = false;
            }
        }

        private async Task RefreshProcessesAsync()
        {
            TxtStatusBar.Text = "Scanning active processes and evaluating heuristics...";
            var list = await Task.Run(() => _procManager.GetRunningProcesses());
            _allProcesses = list;

            int safeCount = list.Count(p => p.SafetyLevel == ProcessSafetyLevel.SafeToTerminate);
            int devCount = list.Count(p => p.SafetyLevel == ProcessSafetyLevel.DeveloperDaemon);
            int userCount = list.Count(p => p.SafetyLevel == ProcessSafetyLevel.UserApplication);
            int critCount = list.Count(p => p.SafetyLevel == ProcessSafetyLevel.SystemCritical);

            if (RbFilterAll != null) RbFilterAll.Content = $"🔘 All ({list.Count})";
            if (RbFilterSafe != null) RbFilterSafe.Content = $"🟢 Safe ({safeCount})";
            if (RbFilterDev != null) RbFilterDev.Content = $"🟠 Dev ({devCount})";
            if (RbFilterUser != null) RbFilterUser.Content = $"🔵 Apps ({userCount})";
            if (RbFilterCrit != null) RbFilterCrit.Content = $"🔴 System ({critCount})";

            ApplyProcessFilter();
            TxtStatusBar.Text = $"Loaded {list.Count} running processes ({safeCount} safe to end). Process heuristics ready.";
        }

        private async void OnRefreshProcessesClick(object sender, RoutedEventArgs e)
        {
            await RefreshProcessesAsync();
        }

        private async void OnTurboBoostClick(object sender, RoutedEventArgs e)
        {
            TxtStatusBar.Text = "Executing Vortex Turbo Boost memory flush across all applications...";
            long reclaimed = await Task.Run(() => _memOptimizer.OptimizeAllWorkingSets());
            UpdateHeaderTelemetry();
            await RefreshProcessesAsync();

            string reclaimedStr = reclaimed > 1024L * 1024L * 1024L
                ? $"{reclaimed / (1024.0 * 1024.0 * 1024.0):F2} GB"
                : $"{reclaimed / (1024.0 * 1024.0):F0} MB";

            TxtStatusBar.Text = $"Turbo Boost Complete! Reclaimed {reclaimedStr} physical RAM.";
            VortexDialogWindow.ShowTurboBoostSuccess(this, reclaimedStr);
        }

        private void OnProcessSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSelectionSummary();
        }

        private void OnTrimSelectedProcessClick(object sender, RoutedEventArgs e)
        {
            var selected = _allProcesses.Where(p => p.IsSelected).ToList();
            if (selected.Count == 0 && LvProcesses.SelectedItem is ProcessInfoModel singleProc)
            {
                selected.Add(singleProc);
            }

            if (selected.Count == 0) return;

            int count = 0;
            foreach (var proc in selected)
            {
                if (_memOptimizer.OptimizeSingleProcess(proc.Id))
                {
                    count++;
                }
            }

            TxtStatusBar.Text = $"Trimmed working set memory for {count} process(es).";
            UpdateHeaderTelemetry();
            VortexDialogWindow.ShowSuccess(this, "Working Sets Trimmed", $"{count} Apps", "Working Sets Paged Out", 
                $"Vortex trimmed working set memory for {count} application(s). Dormant pages moved to standby memory.");
        }

        private async void OnEndSelectedProcessClick(object sender, RoutedEventArgs e)
        {
            var selected = _allProcesses.Where(p => p.IsSelected).ToList();
            if (selected.Count == 0 && LvProcesses.SelectedItem is ProcessInfoModel singleProc)
            {
                selected.Add(singleProc);
            }

            if (selected.Count == 0) return;

            // Protection check for System Critical
            if (selected.Any(p => p.SafetyLevel == ProcessSafetyLevel.SystemCritical))
            {
                VortexDialogWindow.ShowWarning(this, "Action Restricted", "One or more selected processes are Windows System Critical components. They cannot be terminated to preserve OS stability.");
                return;
            }

            // Developer Daemon check
            var devServices = selected.Where(p => p.SafetyLevel == ProcessSafetyLevel.DeveloperDaemon).ToList();
            if (devServices.Count > 0)
            {
                string devNames = string.Join(", ", devServices.Take(3).Select(p => p.DisplayName));
                bool confirmed = VortexDialogWindow.ShowConfirm(this, "Terminate Developer Daemon?", 
                    $"You selected {devServices.Count} active developer daemon(s) ({devNames}). Terminating them may stop local database servers or background developer environments. Are you sure you want to terminate them?",
                    "Terminate Daemons");

                if (!confirmed) return;
            }

            var pids = selected.Select(p => p.Id).ToList();
            TxtStatusBar.Text = $"Terminating {selected.Count} background processes...";

            var (terminated, freed, msgs) = await Task.Run(() => _procManager.TerminateMultipleProcesses(pids));

            string freedStr = freed > 1024L * 1024L * 1024L
                ? $"{freed / (1024.0 * 1024.0 * 1024.0):F2} GB"
                : $"{freed / (1024.0 * 1024.0):F1} MB";

            TxtStatusBar.Text = $"Terminated {terminated} process(es). Freed {freedStr} RAM.";
            UpdateHeaderTelemetry();
            await RefreshProcessesAsync();

            if (terminated > 0)
            {
                VortexDialogWindow.ShowSuccess(this, "Processes Terminated", $"+ {freedStr}", "RAM Reclaimed", 
                    $"Successfully stopped {terminated} background process(es). System memory allocations have been cleared.");
            }
            else if (msgs.Count > 0)
            {
                VortexDialogWindow.ShowWarning(this, "Termination Incomplete", string.Join("\n", msgs.Take(3)));
            }
        }
        #endregion

        #region TAB 2: Storage & Folder Ownership Explorer
        private async Task ScanStorageFolderAsync(string folderPath)
        {
            TxtStatusBar.Text = $"Scanning folder provenance in {folderPath}...";
            var folders = await Task.Run(() => _provenanceEngine.ScanAndIdentifyDirectories(folderPath));
            _allFolders = folders;

            int orphanedCount = folders.Count(f => f.Safety == CleanupSafety.OrphanedLeftover);
            int safeDeleteCount = folders.Count(f => f.Safety == CleanupSafety.SafeToDelete);
            int cacheCount = folders.Count(f => f.Safety == CleanupSafety.SafeToCleanCache);
            int runningCount = folders.Count(f => f.IsRunningProcessLocked);

            if (RbFilterFolderAll != null) RbFilterFolderAll.Content = $"All Folders ({folders.Count})";
            if (RbFilterFolderOrphaned != null) RbFilterFolderOrphaned.Content = $"🟣 Orphaned ({orphanedCount})";
            if (RbFilterFolderSafeDelete != null) RbFilterFolderSafeDelete.Content = $"🟢 Safe to Delete ({safeDeleteCount})";
            if (RbFilterFolderCache != null) RbFilterFolderCache.Content = $"🔵 Safe Caches ({cacheCount})";
            if (RbFilterFolderRunning != null) RbFilterFolderRunning.Content = $"🔴 App Running ({runningCount})";

            ApplyFolderVerdictFilter();
            TxtStatusBar.Text = $"Attributed {folders.Count} directories in {folderPath} ({orphanedCount} orphaned, {safeDeleteCount} safe to delete).";
        }

        private void OnFolderSearchChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtFolderSearchPlaceholder != null)
            {
                TxtFolderSearchPlaceholder.Visibility = string.IsNullOrEmpty(TxtFolderSearch?.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            ApplyFolderVerdictFilter();
        }

        private void OnFolderFilterChanged(object sender, RoutedEventArgs e)
        {
            ApplyFolderVerdictFilter();
        }

        private void ApplyFolderVerdictFilter()
        {
            if (_allFolders == null) return;

            List<FolderAttributionModel> filtered;

            if (RbFilterFolderOrphaned?.IsChecked == true)
            {
                filtered = _allFolders.Where(f => f.Safety == CleanupSafety.OrphanedLeftover).ToList();
            }
            else if (RbFilterFolderSafeDelete?.IsChecked == true)
            {
                filtered = _allFolders.Where(f => f.Safety == CleanupSafety.SafeToDelete).ToList();
            }
            else if (RbFilterFolderCache?.IsChecked == true)
            {
                filtered = _allFolders.Where(f => f.Safety == CleanupSafety.SafeToCleanCache).ToList();
            }
            else if (RbFilterFolderRunning?.IsChecked == true)
            {
                filtered = _allFolders.Where(f => f.IsRunningProcessLocked).ToList();
            }
            else
            {
                filtered = _allFolders.ToList();
            }

            string folderQuery = TxtFolderSearch?.Text?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!string.IsNullOrEmpty(folderQuery))
            {
                filtered = filtered.Where(f =>
                    (f.FolderName != null && f.FolderName.ToLowerInvariant().Contains(folderQuery)) ||
                    (f.Path != null && f.Path.ToLowerInvariant().Contains(folderQuery)) ||
                    (f.OwningApplication != null && f.OwningApplication.ToLowerInvariant().Contains(folderQuery))).ToList();
            }

            LvFolders.ItemsSource = filtered.OrderByDescending(f => f.TotalSizeBytes).ToList();

            long totalBytes = filtered.Sum(f => f.TotalSizeBytes);
            string sizeStr = totalBytes > 1024L * 1024L * 1024L 
                ? $"{totalBytes / (1024.0 * 1024.0 * 1024.0):F2} GB" 
                : $"{totalBytes / (1024.0 * 1024.0):F1} MB";

            if (TxtFolderStats != null)
            {
                TxtFolderStats.Text = $"{filtered.Count} folders ({sizeStr})";
            }

            // Update Clean by Verdict button
            if (BtnCleanVerdict != null)
            {
                if (RbFilterFolderRunning?.IsChecked == true)
                {
                    BtnCleanVerdict.Content = "🔒 App Running (Locked)";
                    BtnCleanVerdict.IsEnabled = false;
                }
                else if (RbFilterFolderOrphaned?.IsChecked == true)
                {
                    int count = filtered.Count(f => !f.IsRunningProcessLocked);
                    BtnCleanVerdict.Content = $"🧹 Clean Orphaned ({count})";
                    BtnCleanVerdict.IsEnabled = count > 0;
                }
                else if (RbFilterFolderSafeDelete?.IsChecked == true)
                {
                    int count = filtered.Count(f => !f.IsRunningProcessLocked);
                    BtnCleanVerdict.Content = $"🧹 Clean Safe ({count})";
                    BtnCleanVerdict.IsEnabled = count > 0;
                }
                else if (RbFilterFolderCache?.IsChecked == true)
                {
                    int count = filtered.Count(f => !f.IsRunningProcessLocked);
                    BtnCleanVerdict.Content = $"🧹 Clean Caches ({count})";
                    BtnCleanVerdict.IsEnabled = count > 0;
                }
                else
                {
                    int count = filtered.Count(f => !f.IsRunningProcessLocked && f.Safety != CleanupSafety.ProtectedSystem && (f.Safety == CleanupSafety.OrphanedLeftover || f.Safety == CleanupSafety.SafeToDelete || f.Safety == CleanupSafety.SafeToCleanCache));
                    BtnCleanVerdict.Content = $"🧹 Clean Filtered ({count})";
                    BtnCleanVerdict.IsEnabled = count > 0;
                }
            }
        }

        private async void OnCleanVerdictFilteredClick(object sender, RoutedEventArgs e)
        {
            if (LvFolders.ItemsSource is not List<FolderAttributionModel> currentView || currentView.Count == 0) return;

            if (RbFilterFolderRunning?.IsChecked == true)
            {
                VortexDialogWindow.ShowWarning(this, "Active Applications Running",
                    "Folders in this filter belong to applications currently running in Task Manager.\n\nPlease close the applications before cleaning to prevent process errors or data loss.");
                return;
            }

            // Select only safe items (not locked by active processes, not protected system)
            var candidates = currentView
                .Where(f => !f.IsRunningProcessLocked && f.Safety != CleanupSafety.ProtectedSystem && (f.Safety == CleanupSafety.OrphanedLeftover || f.Safety == CleanupSafety.SafeToDelete || f.Safety == CleanupSafety.SafeToCleanCache))
                .ToList();

            if (candidates.Count == 0)
            {
                VortexDialogWindow.ShowSuccess(this, "No Folders to Clean", "0 Leftovers", "All Clean",
                    "No safe leftover folders matching the current filter were found in this directory.");
                return;
            }

            long totalBytes = candidates.Sum(f => f.TotalSizeBytes);
            string sizeStr = totalBytes > 1024L * 1024L * 1024L 
                ? $"{totalBytes / (1024.0 * 1024.0 * 1024.0):F2} GB" 
                : $"{totalBytes / (1024.0 * 1024.0):F1} MB";

            string filterName = "Filtered Leftovers";
            if (RbFilterFolderOrphaned?.IsChecked == true) filterName = "Orphaned Leftover Folders";
            else if (RbFilterFolderSafeDelete?.IsChecked == true) filterName = "Safe-to-Delete Folders";
            else if (RbFilterFolderCache?.IsChecked == true) filterName = "Safe Application Caches";

            bool confirmed = VortexDialogWindow.ShowConfirm(this, $"Clean {filterName}",
                $"Found {candidates.Count} safe folder(s) ({sizeStr}) matching your current filter.\n\nWould you like to safely delete them to reclaim disk space?",
                "Clean Selected Folders");
            if (!confirmed) return;

            TxtStatusBar.Text = $"Cleaning {candidates.Count} filtered folders...";
            int cleanedCount = 0;
            long reclaimedBytes = 0;

            await VortexProgressWindow.RunFolderCleanAsync(
                this,
                candidates,
                filterName,
                (cnt, bytes) => { cleanedCount = cnt; reclaimedBytes = bytes; });

            _db.LogAudit("VerdictClean", filterName, reclaimedBytes, $"Deleted {cleanedCount} folders");
            string freedStr = reclaimedBytes > 1024L * 1024L * 1024L
                ? $"{reclaimedBytes / (1024.0 * 1024.0 * 1024.0):F2} GB"
                : $"{reclaimedBytes / (1024.0 * 1024.0):F1} MB";

            string rootDir = Path.GetDirectoryName(candidates.First().Path) ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            await ScanStorageFolderAsync(rootDir);
            UpdateHeaderTelemetry();
        }

        private async void OnScanLocalAppDataClick(object sender, RoutedEventArgs e)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            await ScanStorageFolderAsync(localAppData);
        }

        private async void OnScanAllCachesClick(object sender, RoutedEventArgs e)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            await ScanStorageFolderAsync(appData);
        }

        private void OnOpenFolderInExplorerClick(object sender, RoutedEventArgs e)
        {
            if (LvFolders.SelectedItem is FolderAttributionModel item && Directory.Exists(item.Path))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{item.Path}\"",
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        private async void OnDeleteFolderClick(object sender, RoutedEventArgs e)
        {
            if (LvFolders.SelectedItem is not FolderAttributionModel item) return;

            // Running Process Protection
            if (item.IsRunningProcessLocked)
            {
                VortexDialogWindow.ShowWarning(this, "Active Application Running",
                    $"Cannot delete '{item.FolderName}': The application '{item.ActiveProcessName}' (PID {item.ActiveProcessId}) is currently open and running in task manager.\n\nPlease close the application before attempting to clean this folder to prevent data corruption.");
                return;
            }

            // Protected System Protection
            if (item.Safety == CleanupSafety.ProtectedSystem)
            {
                VortexDialogWindow.ShowWarning(this, "Protected System Component",
                    $"'{item.FolderName}' is a critical Windows operating system component and cannot be deleted.");
                return;
            }

            // User Confirmation
            bool confirmed = VortexDialogWindow.ShowConfirm(this, "Confirm Folder Deletion",
                $"Are you sure you want to permanently delete this folder?\n\nFolder: {item.Path}\nSize: {item.SizeString}\nVerdict: {item.SafetyBadgeText}",
                "Delete Folder");
            if (!confirmed) return;

            TxtStatusBar.Text = $"Deleting folder: {item.Path}...";
            bool success = await Task.Run(() =>
            {
                try
                {
                    if (Directory.Exists(item.Path))
                    {
                        Directory.Delete(item.Path, true);
                        _db.LogAudit("FolderClean", item.FolderName, item.TotalSizeBytes, $"Permanently deleted {item.Path}");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => VortexDialogWindow.ShowWarning(this, "Deletion Failed", $"Could not delete folder: {ex.Message}"));
                }
                return false;
            });

            if (success)
            {
                VortexDialogWindow.ShowSuccess(this, "Folder Deleted", item.SizeString, "Space Reclaimed", $"Successfully removed {item.FolderName}.");
                string parent = Path.GetDirectoryName(item.Path) ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                await ScanStorageFolderAsync(parent);
                UpdateHeaderTelemetry();
            }
        }

        private async void OnCleanOrphanedFoldersClick(object sender, RoutedEventArgs e)
        {
            if (LvFolders.ItemsSource is not List<FolderAttributionModel> folders || folders.Count == 0) return;

            var orphaned = folders.Where(f => f.Safety == CleanupSafety.OrphanedLeftover && !f.IsRunningProcessLocked).ToList();
            if (orphaned.Count == 0)
            {
                VortexDialogWindow.ShowSuccess(this, "No Orphaned Leftovers", "0 Leftovers", "All Clean", "No orphaned leftover folders were found in this directory.");
                return;
            }

            long totalBytes = orphaned.Sum(f => f.TotalSizeBytes);
            string sizeStr = totalBytes > 1024L * 1024L * 1024L 
                ? $"{totalBytes / (1024.0 * 1024.0 * 1024.0):F2} GB" 
                : $"{totalBytes / (1024.0 * 1024.0):F1} MB";

            bool confirmed = VortexDialogWindow.ShowConfirm(this, "Clean Orphaned Leftovers",
                $"Found {orphaned.Count} orphaned folders from uninstalled applications ({sizeStr}).\n\nThese folders no longer have active registry entries. Would you like to safely delete them to reclaim disk space?",
                "Clean All Orphaned");
            if (!confirmed) return;

            TxtStatusBar.Text = $"Cleaning {orphaned.Count} orphaned leftover folders...";
            int cleanedCount = 0;
            long reclaimedBytes = 0;

            await Task.Run(() =>
            {
                foreach (var folder in orphaned)
                {
                    try
                    {
                        if (Directory.Exists(folder.Path))
                        {
                            long sz = folder.TotalSizeBytes;
                            Directory.Delete(folder.Path, true);
                            cleanedCount++;
                            reclaimedBytes += sz;
                        }
                    }
                    catch { }
                }
            });

            _db.LogAudit("OrphanedClean", "Leftover Apps", reclaimedBytes, $"Deleted {cleanedCount} orphaned folders");
            string freedStr = reclaimedBytes > 1024L * 1024L * 1024L 
                ? $"{reclaimedBytes / (1024.0 * 1024.0 * 1024.0):F2} GB" 
                : $"{reclaimedBytes / (1024.0 * 1024.0):F1} MB";

            VortexDialogWindow.ShowSuccess(this, "Orphaned Folders Cleaned", $"+ {freedStr}", "Reclaimed Storage", $"Successfully purged {cleanedCount} leftover folders from uninstalled applications.");
            
            string rootDir = Path.GetDirectoryName(orphaned.First().Path) ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            await ScanStorageFolderAsync(rootDir);
            UpdateHeaderTelemetry();
        }
        #endregion

        #region TAB 3: System & Junk Sweeper
        private async Task ScanJunkAsync()
        {
            TxtStatusBar.Text = "Scanning system temp caches, browser junk, and crash dumps...";
            if (TxtJunkStatus != null) TxtJunkStatus.Text = "Scanning system for junk files...";
            if (PbJunkScan != null) PbJunkScan.Visibility = Visibility.Visible;

            try
            {
                var categories = await Task.Run(() => _systemCleaner.ScanAllJunkCategories());
                _currentJunk = categories;
                LvJunkCategories.ItemsSource = null;
                LvJunkCategories.ItemsSource = categories;

                long totalBytes = categories.Sum(c => c.TotalBytes);
                int totalFiles = categories.Sum(c => c.FileCount);
                string sizeStr = totalBytes > 1024L * 1024L * 1024L 
                    ? $"{totalBytes / (1024.0 * 1024.0 * 1024.0):F2} GB" 
                    : $"{totalBytes / (1024.0 * 1024.0):F0} MB";

                if (TxtJunkStatus != null)
                {
                    TxtJunkStatus.Text = $"Found {sizeStr} safe-to-clean files ({totalFiles:N0} total files).";
                }
                TxtStatusBar.Text = $"Scan complete. {sizeStr} across {totalFiles:N0} files ready for cleanup.";
            }
            finally
            {
                if (PbJunkScan != null) PbJunkScan.Visibility = Visibility.Collapsed;
            }
        }

        private async void OnScanJunkClick(object sender, RoutedEventArgs e)
        {
            if (NavJunk != null && NavJunk.IsChecked != true)
            {
                NavJunk.IsChecked = true;
            }
            await ScanJunkAsync();
        }

        private async void OnCleanJunkClick(object sender, RoutedEventArgs e)
        {
            if (_currentJunk == null || _currentJunk.Count == 0) return;

            var selected = _currentJunk.Where(c => c.IsSelected).ToList();
            if (selected.Count == 0)
            {
                VortexDialogWindow.ShowWarning(this, "No Categories Selected", "Please check at least one category to clean.");
                return;
            }

            if (ChkCreateRestorePoint.IsChecked == true)
            {
                TxtStatusBar.Text = "Creating Windows System Restore point before proceeding...";
                if (TxtJunkStatus != null) TxtJunkStatus.Text = "Creating System Restore point...";
                await Task.Run(() => SystemRestoreService.CreateRestorePoint("Vortex Disk Clean"));
            }

            TxtStatusBar.Text = "Cleaning selected junk categories...";
            if (TxtJunkStatus != null) TxtJunkStatus.Text = "Cleaning files...";

            int deletedFiles = 0;
            long freedBytes = 0;

            await VortexProgressWindow.RunJunkCleanAsync(
                this,
                selected,
                (df, fb) => { deletedFiles = (int)df; freedBytes = fb; });

            _db.LogAudit("SystemClean", "Junk Files", freedBytes, $"Cleaned {deletedFiles} files");
            string freedStr = freedBytes > 1024L * 1024L * 1024L
                ? $"{freedBytes / (1024.0 * 1024.0 * 1024.0):F2} GB"
                : $"{freedBytes / (1024.0 * 1024.0):F0} MB";

            TxtStatusBar.Text = $"Cleanup Complete: Deleted {deletedFiles:N0} files and reclaimed {freedStr} disk space.";

            await ScanJunkAsync();
            UpdateHeaderTelemetry();
        }
        #endregion

        #region TAB 4: Registry Guard
        private async Task ScanRegistryAsync()
        {
            TxtStatusBar.Text = "Scanning Windows Registry for orphaned entries and broken links...";
            if (TxtRegistryStatus != null) TxtRegistryStatus.Text = "Scanning Windows Registry for obsolete keys...";
            if (PbRegistryScan != null) PbRegistryScan.Visibility = Visibility.Visible;

            try
            {
                var issues = await Task.Run(() => _regGuard.ScanRegistryIssues());
                _currentRegIssues = issues;
                LvRegistry.ItemsSource = null;
                LvRegistry.ItemsSource = issues;

                if (TxtRegistryStatus != null)
                {
                    TxtRegistryStatus.Text = issues.Count > 0 
                        ? $"Found {issues.Count} obsolete registry entries ready for safe cleanup." 
                        : "Registry scan complete. No obsolete entries detected.";
                }
                TxtStatusBar.Text = $"Registry scan complete: {issues.Count} obsolete entries detected.";
            }
            finally
            {
                if (PbRegistryScan != null) PbRegistryScan.Visibility = Visibility.Collapsed;
            }
        }

        private async void OnScanRegistryClick(object sender, RoutedEventArgs e)
        {
            await ScanRegistryAsync();
        }

        private async void OnCleanRegistryClick(object sender, RoutedEventArgs e)
        {
            if (_currentRegIssues.Count == 0) return;

            TxtStatusBar.Text = "Exporting .reg backups and removing orphaned keys...";
            int cleaned = await Task.Run(() => _regGuard.CleanSelectedIssues(_currentRegIssues));
            TxtStatusBar.Text = $"Registry Clean Complete: Safely removed {cleaned} orphaned keys. Backups saved.";
            VortexDialogWindow.ShowSuccess(this, "Registry Clean Complete", $"{cleaned} Keys", "Obsolete Entries Removed", 
                "All removed entries have been backed up as a .reg file in %LocalAppData%\\Vortex\\Backups\\");
            await ScanRegistryAsync();
        }
        #endregion

        #region TAB 5: Network Traffic Monitor & Intrusion Shield
        private void RefreshNetworkSockets()
        {
            try
            {
                var conns = _netMonitor.GetAllConnections(includeUdp: true);
                var snap = _netMonitor.GetBandwidthSnapshot();

                // 1. Update Telemetry Metric Cards
                TxtNetDownloadSpeed.Text = snap.DownloadSpeedFormatted;
                TxtNetUploadSpeed.Text = snap.UploadSpeedFormatted;
                TxtNetTotalRecv.Text = $"Total: {snap.TotalReceivedFormatted}";
                TxtNetTotalSent.Text = $"Total: {snap.TotalSentFormatted}";
                TxtNetInterfaceName.Text = $"Adapter: {snap.ActiveInterfaceName}";

                if (snap.IsVpnActive)
                {
                    TxtNetVpnStatus.Text = $"🛡️ VPN: {snap.VpnInterfaceName}";
                    TxtNetVpnStatus.Foreground = (Brush)FindResource("AccentGlowBrush");
                }
                else
                {
                    TxtNetVpnStatus.Text = "🌐 Direct Connection";
                    TxtNetVpnStatus.Foreground = (Brush)FindResource("TextPrimaryBrush");
                }

                int threatCount = conns.Count(c => c.IsSuspicious);
                if (threatCount > 0)
                {
                    TxtNetThreatStatus.Text = $"🚨 {threatCount} Threat{(threatCount > 1 ? "s" : "")} Flagged!";
                    TxtNetThreatStatus.Foreground = (Brush)FindResource("StatusDangerBrush");
                }
                else
                {
                    TxtNetThreatStatus.Text = "✅ 0 Threats";
                    TxtNetThreatStatus.Foreground = (Brush)FindResource("StatusSafeBrush");
                }

                TxtNetActiveSockets.Text = $"Sockets: {conns.Count}";

                // 2. Populate and Filter Table
                _allNetworkConns = conns;
                ApplyNetworkFilter();

                TxtStatusBar.Text = $"Network Shield: {conns.Count} active sockets | ⬇️ {snap.DownloadSpeedFormatted} ⬆️ {snap.UploadSpeedFormatted} | {threatCount} threats.";
            }
            catch (Exception ex)
            {
                TxtStatusBar.Text = $"Network telemetry query error: {ex.Message}";
            }
        }

        private void ApplyNetworkFilter()
        {
            if (_allNetworkConns == null) return;
            string q = TxtNetFilter?.Text?.Trim().ToLowerInvariant() ?? string.Empty;

            if (string.IsNullOrEmpty(q))
            {
                LvNetwork.ItemsSource = _allNetworkConns;
                return;
            }

            LvNetwork.ItemsSource = _allNetworkConns.Where(c =>
                c.ProcessName.ToLowerInvariant().Contains(q) ||
                c.ProcessId.ToString().Contains(q) ||
                c.LocalAddress.ToLowerInvariant().Contains(q) ||
                c.RemoteAddress.ToLowerInvariant().Contains(q) ||
                c.RemoteDomain.ToLowerInvariant().Contains(q) ||
                c.Protocol.ToLowerInvariant().Contains(q) ||
                c.ServiceName.ToLowerInvariant().Contains(q) ||
                c.Direction.ToLowerInvariant().Contains(q) ||
                c.State.ToLowerInvariant().Contains(q) ||
                c.ThreatReason.ToLowerInvariant().Contains(q)).ToList();
        }

        private void OnNetFilterChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtNetFilterPlaceholder != null)
            {
                TxtNetFilterPlaceholder.Visibility = string.IsNullOrEmpty(TxtNetFilter?.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            ApplyNetworkFilter();
        }

        private void OnToggleNetAutoRefreshClick(object sender, RoutedEventArgs e)
        {
            if (_isNetAutoRefreshRunning)
            {
                _netAutoRefreshTimer?.Stop();
                _isNetAutoRefreshRunning = false;
                BtnNetAutoRefresh.Content = "▶️ Auto-Refresh (2s)";
                BtnNetAutoRefresh.Style = (Style)FindResource("VortexSecondaryButton");
                TxtStatusBar.Text = "Network auto-refresh paused.";
            }
            else
            {
                _netAutoRefreshTimer?.Start();
                _isNetAutoRefreshRunning = true;
                BtnNetAutoRefresh.Content = "⏸️ Pause Auto-Refresh";
                BtnNetAutoRefresh.Style = (Style)FindResource("VortexPrimaryButton");
                TxtStatusBar.Text = "Network auto-refresh active (polling every 2s).";
            }
        }

        private void OnRefreshNetworkClick(object sender, RoutedEventArgs e)
        {
            RefreshNetworkSockets();
        }

        private void OnListViewItemPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListViewItem item)
            {
                item.IsSelected = true;
                item.Focus();
            }
        }

        private void OnNetAllowAppClick(object sender, RoutedEventArgs e)
        {
            if (LvNetwork.SelectedItem is NetworkMonitor.NetworkConnectionInfo conn)
            {
                var (success, msg) = _blockService.AllowApp(conn.ProcessName, conn.ProcessPath, conn.ProcessName, "User Application", true);
                TxtStatusBar.Text = msg;
                VortexDialogWindow.ShowSuccess(this, "Application Allowed & Trusted", conn.ProcessName, "Whitelist Rule Active", 
                    $"'{conn.ProcessName}' is now permanently marked as safe.\n• Network intrusion alerts bypassed.\n• Background sockets permitted without warning.");
                RefreshNetworkSockets();
            }
        }

        private void OnNetToggleMuteClick(object sender, RoutedEventArgs e)
        {
            if (LvNetwork.SelectedItem is NetworkMonitor.NetworkConnectionInfo conn)
            {
                var (success, msg) = _blockService.ToggleNotificationMute(conn.ProcessName);
                TxtStatusBar.Text = msg;
                RefreshNetworkSockets();
            }
        }

        private void OnNetKillProcessClick(object sender, RoutedEventArgs e)
        {
            if (LvNetwork.SelectedItem is NetworkMonitor.NetworkConnectionInfo conn)
            {
                if (_blockService.IsProtected(conn.ProcessName))
                {
                    MessageBox.Show($"'{conn.ProcessName}' (PID {conn.ProcessId}) is a protected Windows system component and cannot be terminated.",
                        "Protected System Component", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var result = MessageBox.Show(
                    $"Terminate '{conn.ProcessName}' (PID {conn.ProcessId}) immediately?\n\nExecutable: {conn.ProcessPath}\n{(string.IsNullOrEmpty(conn.ThreatReason) ? "" : "Threat: " + conn.ThreatReason)}",
                    "Confirm Immediate Termination", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    var (success, msg) = _blockService.ForceTerminateProcess(conn.ProcessId, conn.ProcessName);
                    TxtStatusBar.Text = msg;
                    RefreshNetworkSockets();
                }
            }
        }

        private void OnNetBlockFirewallClick(object sender, RoutedEventArgs e)
        {
            if (LvNetwork.SelectedItem is NetworkMonitor.NetworkConnectionInfo conn)
            {
                if (_blockService.IsProtected(conn.ProcessName))
                {
                    MessageBox.Show($"'{conn.ProcessName}' is a protected Windows system component and cannot be blocked.",
                        "Action Blocked by Safety Policy", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(conn.ProcessPath) || !File.Exists(conn.ProcessPath))
                {
                    MessageBox.Show($"Cannot create firewall rule: Executable file path for '{conn.ProcessName}' could not be located on disk.",
                        "Path Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var result = MessageBox.Show(
                    $"Permanently quarantine '{conn.ProcessName}' from network traffic?\n\nExecutable: {conn.ProcessPath}\n\n" +
                    "This creates an inbound + outbound block rule in Windows Advanced Firewall.\n" +
                    "• The application file on your hard drive remains 100% untouched.\n" +
                    "• It will be barred from communicating on LAN or Internet.\n" +
                    "• 100% reversible anytime via the 'Blocked Apps' manager.",
                    "Confirm Permanent Network Quarantine", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var (success, msg) = _blockService.BlockFromInternet(conn.ProcessName, conn.ProcessPath);
                    _blockService.ForceTerminateProcess(conn.ProcessId, conn.ProcessName);
                    MessageBox.Show(msg, success ? "Quarantine Active" : "Quarantine Error", MessageBoxButton.OK,
                        success ? MessageBoxImage.Information : MessageBoxImage.Error);
                    RefreshNetworkSockets();
                }
            }
        }

        private void OnNetBlockExecutionClick(object sender, RoutedEventArgs e)
        {
            if (LvNetwork.SelectedItem is NetworkMonitor.NetworkConnectionInfo conn)
            {
                if (_blockService.IsProtected(conn.ProcessName))
                {
                    MessageBox.Show($"'{conn.ProcessName}' is a protected Windows system component and cannot be blacklisted.",
                        "Action Blocked by Safety Policy", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var result = MessageBox.Show(
                    $"Permanently blacklist '{conn.ProcessName}' from running?\n\nExecutable: {conn.ProcessPath}\n\n" +
                    "Windows will automatically intercept and abort this application whenever anything tries to launch it.\n" +
                    "• The application file on your hard drive remains 100% untouched.\n" +
                    "• Survives PC reboots, service restarts, and background tasks.\n" +
                    "• 100% reversible anytime via the 'Blocked Apps' manager.",
                    "Confirm Permanent Execution Blacklist", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    var (success, msg) = _blockService.BlockExecution(conn.ProcessName, conn.ProcessPath);
                    MessageBox.Show(msg, success ? "Blacklist Active" : "Blacklist Error", MessageBoxButton.OK,
                        success ? MessageBoxImage.Information : MessageBoxImage.Error);
                    RefreshNetworkSockets();
                }
            }
        }

        private void OnOpenBlockedAppsClick(object sender, RoutedEventArgs e)
        {
            var win = new BlockedAppsWindow(_blockService) { Owner = this };
            win.ShowDialog();
            RefreshNetworkSockets();
        }

        private void OnNetOpenFileLocationClick(object sender, RoutedEventArgs e)
        {
            if (LvNetwork.SelectedItem is NetworkMonitor.NetworkConnectionInfo conn)
            {
                if (!_netMonitor.OpenProcessLocation(conn.ProcessId))
                {
                    MessageBox.Show("Could not open file location. The process executable may be a protected system component or access was denied.",
                        "File Location", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void OnNetCopyEndpointClick(object sender, RoutedEventArgs e)
        {
            if (LvNetwork.SelectedItem is NetworkMonitor.NetworkConnectionInfo conn)
            {
                string text = conn.DisplayRemote;
                if (!string.IsNullOrEmpty(text) && text != "-")
                {
                    Clipboard.SetText(text);
                    TxtStatusBar.Text = $"Copied remote endpoint '{text}' to clipboard.";
                }
            }
        }

        private void OnNetInspectProcessClick(object sender, RoutedEventArgs e)
        {
            if (LvNetwork.SelectedItem is NetworkMonitor.NetworkConnectionInfo conn)
            {
                NavProcesses.IsChecked = true;
                var match = _allProcesses.FirstOrDefault(p => p.Id == conn.ProcessId);
                if (match != null)
                {
                    LvProcesses.SelectedItem = match;
                    LvProcesses.ScrollIntoView(match);
                }
            }
        }

        #region Network Shield Sub-Tabs & App Rules Manager
        private void OnNetSubTabChanged(object sender, RoutedEventArgs e)
        {
            if (ViewNetSockets == null || ViewNetDiscover == null || ViewNetAllowed == null || ViewNetBlocked == null) return;

            ViewNetSockets.Visibility = SubTabNetSockets.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ViewNetDiscover.Visibility = SubTabNetDiscover.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ViewNetAllowed.Visibility = SubTabNetAllowed.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ViewNetBlocked.Visibility = SubTabNetBlocked.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

            if (SubTabNetSockets.IsChecked == true)
            {
                RefreshNetworkSockets();
            }
            else if (SubTabNetDiscover.IsChecked == true)
            {
                if (_allNetDiscoveredApps.Count == 0) _ = RefreshNetDiscoverAppsAsync();
            }
            else if (SubTabNetAllowed.IsChecked == true)
            {
                RefreshNetAllowedApps();
            }
            else if (SubTabNetBlocked.IsChecked == true)
            {
                RefreshNetBlockedApps();
            }
        }

        private async Task RefreshNetDiscoverAppsAsync()
        {
            TxtStatusBar.Text = "Scanning installed applications and active PC processes...";
            await Task.Run(() =>
            {
                _allNetDiscoveredApps = _blockService.GetDiscoveredApps();
                _allNetAllowedApps = _blockService.GetAllowedApps();
                _allNetBlockedApps = _blockService.GetBlockedItems();
            });

            ApplyNetDiscoverFilter();
            UpdateNetSubTabBadges();
            int runningCount = _allNetDiscoveredApps.Count(a => a.IsRunning);
            TxtStatusBar.Text = $"Detected {_allNetDiscoveredApps.Count} PC applications ({runningCount} active right now).";
        }

        private void UpdateNetSubTabBadges()
        {
            if (SubTabNetAllowed != null) SubTabNetAllowed.Content = $"🛡️ Allowed Apps ({_allNetAllowedApps.Count})";
            if (SubTabNetBlocked != null) SubTabNetBlocked.Content = $"🚫 Blocked Apps ({_allNetBlockedApps.Count})";
        }

        private void RefreshNetAllowedApps()
        {
            _allNetAllowedApps = _blockService.GetAllowedApps();
            ApplyNetAllowedFilter();
            UpdateNetSubTabBadges();
        }

        private void RefreshNetBlockedApps()
        {
            _allNetBlockedApps = _blockService.GetBlockedItems();
            ApplyNetBlockedFilter();
            UpdateNetSubTabBadges();
        }

        private void ApplyNetDiscoverFilter()
        {
            if (_allNetDiscoveredApps == null) return;
            string q = TxtNetDiscoverFilter?.Text?.Trim().ToLowerInvariant() ?? string.Empty;

            if (string.IsNullOrEmpty(q))
            {
                LvNetDiscoverApps.ItemsSource = _allNetDiscoveredApps;
                return;
            }

            LvNetDiscoverApps.ItemsSource = _allNetDiscoveredApps.Where(a =>
                a.DisplayName.ToLowerInvariant().Contains(q) ||
                a.ProcessName.ToLowerInvariant().Contains(q) ||
                a.Category.ToLowerInvariant().Contains(q) ||
                a.ExecutablePath.ToLowerInvariant().Contains(q)).ToList();
        }

        private void OnNetDiscoverFilterChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtNetDiscoverFilterPlaceholder != null)
            {
                TxtNetDiscoverFilterPlaceholder.Visibility = string.IsNullOrEmpty(TxtNetDiscoverFilter?.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            ApplyNetDiscoverFilter();
        }

        private async void OnNetRescanAppsClick(object sender, RoutedEventArgs e)
        {
            await RefreshNetDiscoverAppsAsync();
        }

        private void OnNetBrowseCustomAppClick(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Application Executable to Allow & Trust",
                Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dlg.ShowDialog(this) == true)
            {
                string path = dlg.FileName;
                string fileName = Path.GetFileName(path);
                string appName = Path.GetFileNameWithoutExtension(path);

                var (success, msg) = _blockService.AllowApp(fileName, path, appName, "User Application", true);
                VortexDialogWindow.ShowSuccess(this, "Application Added to Trusted List", appName, "Whitelist Rule Active",
                    $"'{appName}' ({fileName}) is now marked as safe and trusted.\n• Path: {path}");

                _ = RefreshNetDiscoverAppsAsync();
            }
        }

        private void OnNetAllowDiscoverClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is DiscoveredAppModel item)
            {
                var (success, msg) = _blockService.AllowApp(item.ProcessName, item.ExecutablePath, item.DisplayName, item.Category, true);
                item.AccessStatus = "ALLOWED";
                item.MuteNotifications = true;
                _allNetAllowedApps = _blockService.GetAllowedApps();
                UpdateNetSubTabBadges();
                ApplyNetDiscoverFilter();
                TxtStatusBar.Text = $"'{item.DisplayName}' is now allowed and trusted.";
            }
        }

        private void OnNetBlockDiscoverClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is DiscoveredAppModel item)
            {
                if (_blockService.IsProtected(item.ProcessName))
                {
                    MessageBox.Show($"'{item.ProcessName}' is a protected Windows system component and cannot be blocked.",
                        "Safety Policy", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(item.ExecutablePath) || !File.Exists(item.ExecutablePath))
                {
                    MessageBox.Show($"Cannot create firewall rule: Executable file path for '{item.DisplayName}' could not be located on disk.",
                        "Path Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var (success, msg) = _blockService.BlockFromInternet(item.ProcessName, item.ExecutablePath);
                if (success)
                {
                    item.AccessStatus = "BLOCKED_FIREWALL";
                    _allNetBlockedApps = _blockService.GetBlockedItems();
                    UpdateNetSubTabBadges();
                    ApplyNetDiscoverFilter();
                    TxtStatusBar.Text = $"'{item.DisplayName}' is now blocked from network traffic via Windows Firewall.";
                }
                else
                {
                    MessageBox.Show(msg, "Quarantine Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OnNetResetDiscoverClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is DiscoveredAppModel item)
            {
                _blockService.ResetAppStatus(item.ProcessName);
                item.AccessStatus = "UNCONFIGURED";
                item.MuteNotifications = false;
                _allNetAllowedApps = _blockService.GetAllowedApps();
                _allNetBlockedApps = _blockService.GetBlockedItems();
                UpdateNetSubTabBadges();
                ApplyNetDiscoverFilter();
                TxtStatusBar.Text = $"Reset status for '{item.DisplayName}'. Alerts are now active.";
            }
        }

        private void OnNetToggleNotificationDiscoverClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is DiscoveredAppModel item)
            {
                _blockService.ToggleNotificationMute(item.ProcessName);
                item.MuteNotifications = !item.MuteNotifications;
                ApplyNetDiscoverFilter();
                TxtStatusBar.Text = $"Notification alerts for '{item.DisplayName}' set to: {(item.MuteNotifications ? "Muted 🔇" : "Active 🔔")}.";
            }
        }

        private void ApplyNetAllowedFilter()
        {
            if (_allNetAllowedApps == null) return;
            string q = TxtNetAllowedFilter?.Text?.Trim().ToLowerInvariant() ?? string.Empty;

            if (string.IsNullOrEmpty(q))
            {
                LvNetAllowedApps.ItemsSource = _allNetAllowedApps;
                return;
            }

            LvNetAllowedApps.ItemsSource = _allNetAllowedApps.Where(a =>
                a.DisplayName.ToLowerInvariant().Contains(q) ||
                a.ProcessName.ToLowerInvariant().Contains(q) ||
                a.Category.ToLowerInvariant().Contains(q) ||
                a.ExecutablePath.ToLowerInvariant().Contains(q)).ToList();
        }

        private void OnNetAllowedFilterChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtNetAllowedFilterPlaceholder != null)
            {
                TxtNetAllowedFilterPlaceholder.Visibility = string.IsNullOrEmpty(TxtNetAllowedFilter?.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            ApplyNetAllowedFilter();
        }

        private void OnNetToggleNotificationAllowedClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AppAccessRule rule)
            {
                _blockService.ToggleNotificationMute(rule.ProcessName);
                RefreshNetAllowedApps();
            }
        }

        private void OnNetRemoveAllowedClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AppAccessRule rule)
            {
                _blockService.RevokeAllowApp(rule.ProcessName);
                RefreshNetAllowedApps();
                TxtStatusBar.Text = $"Revoked whitelist status for '{rule.DisplayName}'.";
            }
        }

        private void ApplyNetBlockedFilter()
        {
            if (_allNetBlockedApps == null) return;
            string q = TxtNetBlockedFilter?.Text?.Trim().ToLowerInvariant() ?? string.Empty;

            if (string.IsNullOrEmpty(q))
            {
                LvNetBlockedApps.ItemsSource = _allNetBlockedApps;
                return;
            }

            LvNetBlockedApps.ItemsSource = _allNetBlockedApps.Where(b =>
                b.ProcessName.ToLowerInvariant().Contains(q) ||
                b.ExecutablePath.ToLowerInvariant().Contains(q) ||
                b.BlockTypeDisplay.ToLowerInvariant().Contains(q)).ToList();
        }

        private void OnNetBlockedFilterChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtNetBlockedFilterPlaceholder != null)
            {
                TxtNetBlockedFilterPlaceholder.Visibility = string.IsNullOrEmpty(TxtNetBlockedFilter?.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            ApplyNetBlockedFilter();
        }

        private void OnNetUnblockRowClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ProcessBlockService.BlockedItemModel item)
            {
                if (item.BlockType == "FIREWALL")
                {
                    var (success, msg) = _blockService.UnblockInternet(item.ProcessName);
                    TxtStatusBar.Text = msg;
                }
                else if (item.BlockType == "EXECUTION")
                {
                    var (success, msg) = _blockService.UnblockExecution(item.ProcessName);
                    TxtStatusBar.Text = msg;
                }
                else
                {
                    _blockService.ResetAppStatus(item.ProcessName);
                    TxtStatusBar.Text = $"Unblocked {item.ProcessName}.";
                }

                var match = _allNetDiscoveredApps.FirstOrDefault(d => d.ProcessName.Equals(item.ProcessName, StringComparison.OrdinalIgnoreCase));
                if (match != null) match.AccessStatus = "UNCONFIGURED";

                RefreshNetBlockedApps();
                ApplyNetDiscoverFilter();
            }
        }
        #endregion
        #endregion

        #region TAB 6: Services Controller
        private List<ServiceItemModel> _allServices = new();

        private void RefreshServicesList()
        {
            TxtStatusBar.Text = "Enumerating Windows services via sc.exe...";
            _allServices = _svcController.GetOptimizableServices();
            ApplyServicesFilter();
            TxtStatusBar.Text = $"Loaded {_allServices.Count} optimizable background services.";
        }

        private void OnServiceFilterChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtServiceFilterPlaceholder != null)
            {
                TxtServiceFilterPlaceholder.Visibility = string.IsNullOrEmpty(TxtServiceFilter?.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            ApplyServicesFilter();
        }

        private void ApplyServicesFilter()
        {
            if (_allServices == null) return;
            string q = TxtServiceFilter?.Text?.Trim().ToLowerInvariant() ?? string.Empty;
            if (string.IsNullOrEmpty(q))
            {
                LvServices.ItemsSource = _allServices;
            }
            else
            {
                LvServices.ItemsSource = _allServices.Where(s =>
                    (s.ServiceName != null && s.ServiceName.ToLowerInvariant().Contains(q)) ||
                    (s.DisplayName != null && s.DisplayName.ToLowerInvariant().Contains(q)) ||
                    (s.Status != null && s.Status.ToLowerInvariant().Contains(q)) ||
                    (s.Impact != null && s.Impact.ToLowerInvariant().Contains(q)) ||
                    (s.Recommendation != null && s.Recommendation.ToLowerInvariant().Contains(q))).ToList();
            }
        }

        private void OnRefreshServicesClick(object sender, RoutedEventArgs e)
        {
            RefreshServicesList();
        }

        private void OnToggleServiceRowClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ServiceItemModel svc)
            {
                bool targetStart = !svc.IsRunning;
                var (success, msg) = _svcController.ToggleService(svc.ServiceName, targetStart);
                TxtStatusBar.Text = msg;
                RefreshServicesList();
            }
        }
        #endregion

        #region TAB 7: Gaming & Focus Mode
        private void OnToggleFocusModeClick(object sender, RoutedEventArgs e)
        {
            if (_focusManager.IsFocusModeActive)
            {
                string msg = _focusManager.DisableFocusMode();
                BtnToggleFocusMode.Content = "🚀 ACTIVATE DEEP FOCUS MODE";
                TxtFocusStatus.Text = "Focus Mode is currently OFF.";
                TxtStatusBar.Text = msg;
            }
            else
            {
                var (stopped, msg) = _focusManager.EnableFocusMode();
                BtnToggleFocusMode.Content = "🛑 DEACTIVATE FOCUS MODE";
                TxtFocusStatus.Text = $"Focus Mode ACTIVE: Paused {stopped} background tasks.";
                TxtStatusBar.Text = msg;
            }
        }
        #endregion

        #region TAB 8: Audit & History
        private void RefreshHistoryLogs()
        {
            var logs = _db.GetRecentAudits();
            LvHistory.ItemsSource = logs;
            TxtStatusBar.Text = $"Loaded {logs.Count} audit log entries from SQLite database.";
        }

        private void OnRefreshHistoryClick(object sender, RoutedEventArgs e)
        {
            RefreshHistoryLogs();
        }
        #endregion

        #region TAB 9: Privacy Eraser
        private async Task ScanPrivacyTracesAsync()
        {
            PbPrivacyScan.Visibility = Visibility.Visible;
            TxtStatusBar.Text = "Scanning for privacy traces...";

            var traces = await Task.Run(() => _privacyEraser.ScanAllTraces());

            LvPrivacy.ItemsSource = traces;
            PbPrivacyScan.Visibility = Visibility.Collapsed;

            int total = traces.Sum(t => t.ItemCount);
            TxtStatusBar.Text = $"Privacy scan complete: Found {total} trace items across {traces.Count} categories.";
        }

        private void OnScanPrivacyClick(object sender, RoutedEventArgs e)
        {
            LvPrivacy.ItemsSource = null;
            _ = ScanPrivacyTracesAsync();
        }

        private async void OnErasePrivacyClick(object sender, RoutedEventArgs e)
        {
            var items = LvPrivacy.ItemsSource as List<PrivacyTraceItem>;
            if (items == null || !items.Any(i => i.IsSelected)) return;

            var selected = items.Where(i => i.IsSelected).ToList();
            TxtStatusBar.Text = $"Erasing {selected.Count} privacy trace categories...";

            var win = new VortexProgressWindow(
                selected.Count,
                "Erasing Privacy Traces",
                "Wiping OS activity traces from your system",
                "🕵️");
            win.Owner = this;
            win.Show();

            int totalWiped = 0;
            long totalFreed = 0;

            foreach (var item in selected)
            {
                var (wiped, freed) = await Task.Run(() => _privacyEraser.EraseSelected(new[] { item }));
                totalWiped += wiped;
                totalFreed += freed;
                win.ReportItem(item.Name, freed);
                await Task.Delay(30);
            }

            win.MarkComplete("Privacy Traces Wiped");
            _db.LogAudit("PrivacyErase", "Traces", totalFreed, $"Wiped {totalWiped} trace items");
            TxtStatusBar.Text = $"Privacy erase complete: {totalWiped} items wiped.";
            _ = ScanPrivacyTracesAsync();
        }
        #endregion

        #region TAB 10: Identity Shield
        private async Task ScanIdentityAsync()
        {
            PbIdentityScan.Visibility = Visibility.Visible;
            TxtStatusBar.Text = "Scanning for identity exposure...";

            var findings = await Task.Run(() => _identityScanner.ScanAll());

            LvIdentity.ItemsSource = findings;
            PbIdentityScan.Visibility = Visibility.Collapsed;

            int critical = findings.Count(f => f.Risk == PrivacyRiskLevel.Critical);
            int high = findings.Count(f => f.Risk == PrivacyRiskLevel.High);
            TxtStatusBar.Text = $"Identity scan complete: {findings.Count} findings ({critical} critical, {high} high risk).";
        }

        private void OnScanIdentityClick(object sender, RoutedEventArgs e)
        {
            LvIdentity.ItemsSource = null;
            _ = ScanIdentityAsync();
        }

        private void OnIdentityActionClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string target && !string.IsNullOrEmpty(target))
            {
                try
                {
                    if (target.StartsWith("ms-settings:"))
                    {
                        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                    }
                    else if (Directory.Exists(target))
                    {
                        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = true });
                    }
                    else if (File.Exists(target))
                    {
                        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{target}\"") { UseShellExecute = true });
                    }
                    else
                    {
                        // Supports commands with arguments, like "control.exe keymgr.dll" or "SystemPropertiesAdvanced.exe"
                        var parts = target.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                        var fileName = parts[0];
                        var args = parts.Length > 1 ? parts[1] : string.Empty;
                        Process.Start(new ProcessStartInfo(fileName, args) { UseShellExecute = true });
                    }
                }
                catch (Exception ex)
                {
                    TxtStatusBar.Text = $"Failed to open: {ex.Message}";
                }
            }
        }
        #endregion

        #region TAB 11: Software Updater
        private List<WingetPackage> _currentUpdates = new();
        private CancellationTokenSource? _updaterCts;

        private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
        {
            if (!_wingetService.IsWingetAvailable())
            {
                TxtUpdaterStatus.Text = "❌ winget is not available. Install 'App Installer' from the Microsoft Store.";
                return;
            }

            PbUpdaterScan.Visibility = Visibility.Visible;
            TxtUpdaterStatus.Text = "Checking for available updates...";
            LvUpdater.ItemsSource = null;

            _currentUpdates = await _wingetService.GetAvailableUpdatesAsync();

            LvUpdater.ItemsSource = _currentUpdates;
            PbUpdaterScan.Visibility = Visibility.Collapsed;

            int known = _currentUpdates.Count(p => !p.IsUnknown);
            int unknown = _currentUpdates.Count(p => p.IsUnknown);
            TxtUpdaterStatus.Text = $"Found {_currentUpdates.Count} updates available ({known} known, {unknown} unknown).";
        }

        private void OnSelectAllKnownClick(object sender, RoutedEventArgs e)
        {
            foreach (var pkg in _currentUpdates)
                pkg.IsSelected = !pkg.IsUnknown;
            LvUpdater.ItemsSource = null;
            LvUpdater.ItemsSource = _currentUpdates;
        }

        private void OnSelectAllHeaderClick(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk && _currentUpdates != null)
            {
                bool isChecked = chk.IsChecked == true;
                foreach (var pkg in _currentUpdates)
                    pkg.IsSelected = isChecked && !pkg.IsUnknown;
                LvUpdater.ItemsSource = null;
                LvUpdater.ItemsSource = _currentUpdates;
            }
        }

        private void OnStopUpdateClick(object sender, RoutedEventArgs e)
        {
            if (_updaterCts != null && !_updaterCts.IsCancellationRequested)
            {
                TxtUpdaterStatus.Text = "Stopping updates... cancelling running process.";
                BtnStopUpdater.IsEnabled = false;
                _updaterCts.Cancel();
            }
        }

        private async void OnCleanDownloadCacheClick(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnCleanCache.IsEnabled = false;
                TxtUpdaterStatus.Text = "Cleaning temporary download cache...";

                var (bytesFreed, filesDeleted) = await Task.Run(() => WingetService.ClearDownloadCache());

                double mb = bytesFreed / (1024.0 * 1024.0);
                string freedText = mb >= 1.0 ? $"{mb:F1} MB" : $"{bytesFreed / 1024.0:F0} KB";

                if (filesDeleted > 0 || bytesFreed > 0)
                {
                    TxtUpdaterStatus.Text = $"🧹 Download cache cleaned: Freed {freedText} across {filesDeleted} temporary files.";
                }
                else
                {
                    TxtUpdaterStatus.Text = "🧹 Download cache is already clean (0 bytes in temporary installer folders).";
                }
            }
            catch (Exception ex)
            {
                TxtUpdaterStatus.Text = $"Error cleaning download cache: {ex.Message}";
            }
            finally
            {
                BtnCleanCache.IsEnabled = true;
            }
        }

        private async void OnUpdateSelectedClick(object sender, RoutedEventArgs e)
        {
            var selected = _currentUpdates.Where(p => p.IsSelected).ToList();
            if (selected.Count == 0) return;

            _updaterCts = new CancellationTokenSource();

            BtnUpdateSelected.IsEnabled = false;
            BtnStopUpdater.Visibility = Visibility.Visible;
            BtnStopUpdater.IsEnabled = true;
            BorderUpdaterProgress.Visibility = Visibility.Visible;
            BorderUpdaterTerminal.Visibility = Visibility.Visible;
            TxtUpdaterOutput.Text = string.Empty;

            PbOverallProgress.Value = 0;
            TxtOverallProgressPercent.Text = "0%";
            TxtOverallProgressTitle.Text = $"Overall Progress: 0 of {selected.Count} completed";

            int success = 0, failed = 0;

            void AppendLine(string line)
            {
                Dispatcher.Invoke(() =>
                {
                    TxtUpdaterOutput.Text += $"[{DateTime.Now:HH:mm:ss}] {line}\n";
                    SvUpdaterOutput.ScrollToEnd();
                });
            }

            var textProgress = new Progress<string>(AppendLine);

            try
            {
                for (int i = 0; i < selected.Count; i++)
                {
                    if (_updaterCts.IsCancellationRequested) break;

                    var pkg = selected[i];
                    int currentIdx = i + 1;
                    double overallPct = ((double)i / selected.Count) * 100.0;

                    TxtOverallProgressTitle.Text = $"Overall Progress: Updating package {currentIdx} of {selected.Count} ({pkg.Name})";
                    TxtOverallProgressPercent.Text = $"{overallPct:F0}%";
                    PbOverallProgress.Value = overallPct;

                    TxtPackageProgressTitle.Text = $"Downloading {pkg.Name}...";
                    TxtPackageProgressDetail.Text = "Connecting...";
                    PbPackageProgress.IsIndeterminate = true;
                    PbPackageProgress.Value = 0;

                    AppendLine($"▶ [{currentIdx}/{selected.Count}] Starting update: {pkg.Name} ({pkg.Id}) {pkg.CurrentVer} → {pkg.LatestVer}");
                    TxtUpdaterStatus.Text = $"Updating {pkg.Name} ({currentIdx} of {selected.Count})...";

                    var downloadProgress = new Progress<WingetProgressUpdate>(update =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (!string.IsNullOrEmpty(update.StatusText))
                            {
                                TxtPackageProgressTitle.Text = $"{update.StatusText} ({pkg.Name})";
                            }

                            if (!string.IsNullOrEmpty(update.DownloadedText))
                            {
                                TxtPackageProgressDetail.Text = update.DownloadedText;
                            }
                            else if (update.Percent.HasValue)
                            {
                                TxtPackageProgressDetail.Text = $"{update.Percent.Value:F0}%";
                            }

                            if (update.Percent.HasValue)
                            {
                                PbPackageProgress.IsIndeterminate = false;
                                PbPackageProgress.Value = update.Percent.Value;
                            }
                        });
                    });

                    bool ok = await _wingetService.UpgradePackageAsync(pkg.Id, pkg.Source, textProgress, downloadProgress, _updaterCts.Token);

                    if (ok)
                    {
                        success++;
                        pkg.IsSelected = false; // completed, uncheck it
                        pkg.CurrentVer = pkg.LatestVer;
                        AppendLine($"  ✅ {pkg.Name} updated successfully.\n");
                        TxtPackageProgressTitle.Text = $"✅ {pkg.Name} updated successfully!";
                        TxtPackageProgressDetail.Text = "Complete";
                        PbPackageProgress.IsIndeterminate = false;
                        PbPackageProgress.Value = 100;
                    }
                    else
                    {
                        failed++;
                        if (_updaterCts.IsCancellationRequested)
                        {
                            AppendLine($"  ⏹ {pkg.Name} cancelled by user.\n");
                            TxtPackageProgressTitle.Text = "⏹ Update cancelled";
                            TxtPackageProgressDetail.Text = "Stopped";
                            PbPackageProgress.IsIndeterminate = false;
                            break;
                        }
                        else
                        {
                            AppendLine($"  ❌ {pkg.Name} update failed or completed with warnings.\n");
                            TxtPackageProgressTitle.Text = $"❌ {pkg.Name} failed";
                            TxtPackageProgressDetail.Text = "Check log";
                            PbPackageProgress.IsIndeterminate = false;
                        }
                    }
                }
            }
            finally
            {
                BtnUpdateSelected.IsEnabled = true;
                BtnStopUpdater.Visibility = Visibility.Collapsed;
                BtnStopUpdater.IsEnabled = true;

                if (_updaterCts?.IsCancellationRequested == true)
                {
                    TxtOverallProgressTitle.Text = "Update halted by user";
                    TxtUpdaterStatus.Text = $"Update cancelled. ({success} updated, {failed} failed, {selected.Count - success - failed} cancelled).";
                    AppendLine($"⏹ Update process cancelled by user. ({success} succeeded, {failed} failed).\n");
                }
                else
                {
                    TxtOverallProgressTitle.Text = $"All selected updates processed ({success} succeeded, {failed} failed)";
                    TxtOverallProgressPercent.Text = "100%";
                    PbOverallProgress.Value = 100;

                    TxtUpdaterStatus.Text = $"Update complete: {success} updated, {failed} failed, {_currentUpdates.Count - selected.Count} skipped.";
                }

                _db.LogAudit("SoftwareUpdate", "winget", 0, $"Updated {success} apps, {failed} failed");

                LvUpdater.ItemsSource = null;
                LvUpdater.ItemsSource = _currentUpdates;

                // Automatically clean up any installer files left behind after update finishes
                _ = Task.Run(() => WingetService.ClearDownloadCache());

                _updaterCts?.Dispose();
                _updaterCts = null;
            }
        }
        #endregion

        #region TAB 12: System Info
        private async Task LoadSystemInfoAsync()
        {
            if (_specsCpu != null) return; // already loaded

            TxtStatusBar.Text = "Loading hardware specifications...";

            await Task.Run(() =>
            {
                _specsCpu     = _hardwareMonitor.GetCpuSpecs();
                _specsGpu     = _hardwareMonitor.GetGpuSpecs();
                _specsMem     = _hardwareMonitor.GetMemorySpecs();
                _specsStorage = _hardwareMonitor.GetStorageSpecs();
                _specsMb      = _hardwareMonitor.GetMotherboardSpecs();
                _specsOs      = _hardwareMonitor.GetOsAndSoftwareSpecs();
            });

            // Default to CPU tab
            LvSysInfo.ItemsSource = _specsCpu;
            TxtStatusBar.Text = "Hardware specs loaded. Sensor monitoring active.";
        }

        private void OnSysInfoTabChanged(object sender, RoutedEventArgs e)
        {
            if (LvSysInfo == null) return;

            if (SysTabCpu.IsChecked == true)       LvSysInfo.ItemsSource = _specsCpu;
            else if (SysTabGpu.IsChecked == true)  LvSysInfo.ItemsSource = _specsGpu;
            else if (SysTabRam.IsChecked == true)  LvSysInfo.ItemsSource = _specsMem;
            else if (SysTabDisk.IsChecked == true) LvSysInfo.ItemsSource = _specsStorage;
            else if (SysTabMb.IsChecked == true)   LvSysInfo.ItemsSource = _specsMb;
            else if (SysTabOs.IsChecked == true)   LvSysInfo.ItemsSource = _specsOs;
        }

        private void StartSensorTimer()
        {
            if (_sensorTimer != null) return;
            _sensorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _sensorTimer.Tick += (_, _) => UpdateSensorCards();
            _sensorTimer.Start();
            UpdateSensorCards(); // immediate first read
        }

        private void UpdateSensorCards()
        {
            try
            {
                var r = _hardwareMonitor.GetLiveReading();

                TxtCpuTemp.Text    = r.CpuTempC > 0  ? $"{r.CpuTempC:F0} °C"  : "N/A";
                TxtCpuUsage.Text   = $"Usage: {r.CpuUsagePct:F0}%";
                TxtGpuTemp.Text    = r.GpuTempC > 0  ? $"{r.GpuTempC:F0} °C"  : "N/A";
                TxtGpuUsage.Text   = $"Usage: {r.GpuUsagePct:F0}%";
                TxtDiskTemp.Text   = r.DiskTempC > 0 ? $"{r.DiskTempC:F0} °C" : "N/A";
                TxtMbTemp.Text     = r.MbTempC > 0   ? $"MB: {r.MbTempC:F0} °C"   : "MB: N/A";
                TxtSysRamPct.Text  = $"{r.RamUsagePct:F0}%";
                TxtSysRamUsed.Text = $"{r.RamUsedBytes / (1024.0 * 1024 * 1024):F1} / {r.RamTotalBytes / (1024.0 * 1024 * 1024):F1} GB";

                // Temperature alerts
                if (r.AnyCritical)
                {
                    string alert = "";
                    if (r.CpuTempC >= 95) alert += $"CPU {r.CpuTempC:F0}°C  ";
                    if (r.GpuTempC >= 90) alert += $"GPU {r.GpuTempC:F0}°C  ";
                    if (r.DiskTempC >= 60) alert += $"Disk {r.DiskTempC:F0}°C  ";
                    if (r.MbTempC >= 80)  alert += $"MB {r.MbTempC:F0}°C  ";
                    TxtStatusBar.Text = $"🔴 CRITICAL TEMPERATURE: {alert.Trim()}";

                    _trayService?.ShowBalloon("⚠️ Overheat Alert", $"Critical temperature detected: {alert.Trim()}", 3);
                }
                else if (r.AnyWarning)
                {
                    string warn = "";
                    if (r.IsCpuWarning)  warn += $"CPU {r.CpuTempC:F0}°C  ";
                    if (r.IsGpuWarning)  warn += $"GPU {r.GpuTempC:F0}°C  ";
                    if (r.IsDiskWarning) warn += $"Disk {r.DiskTempC:F0}°C  ";
                    if (r.IsMbWarning)   warn += $"MB {r.MbTempC:F0}°C  ";
                    TxtStatusBar.Text = $"🟡 Temperature Warning: {warn.Trim()}";
                }
            }
            catch { }
        }

        private void OnRefreshSysInfoClick(object sender, RoutedEventArgs e)
        {
            _specsCpu = null; // force reload
            _ = LoadSystemInfoAsync();
        }

        private void OnExportSysInfoClick(object sender, RoutedEventArgs e)
        {
            if (_specsCpu == null) return;

            var reading = _hardwareMonitor.GetLiveReading();
            var report = _hardwareMonitor.GenerateTxtReport(
                _specsCpu!, _specsGpu!, _specsMem!, _specsStorage!, _specsMb!, _specsOs!, reading);

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export System Info Report",
                Filter = "Text Files|*.txt",
                FileName = $"Vortex_SystemInfo_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            };

            if (dlg.ShowDialog() == true)
            {
                File.WriteAllText(dlg.FileName, report);
                TxtStatusBar.Text = $"Report exported to: {dlg.FileName}";
            }
        }
        #endregion

        #region TAB 10B: Data Shield & Zero-Trust File Vault
        private void OnVaultSubTabChanged(object sender, RoutedEventArgs e)
        {
            if (ViewVaultFolders == null || ViewVaultUsb == null || ViewVaultSettings == null) return;

            ViewVaultFolders.Visibility = SubTabVaultFolders.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ViewVaultUsb.Visibility = SubTabVaultUsb.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ViewVaultSettings.Visibility = SubTabVaultSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

            RefreshVaultView();
        }

        private void RefreshVaultView()
        {
            try
            {
                if (_folderLockService == null || _usbDlp == null || TxtVaultCount == null) return;

                _allVaultFolders = _folderLockService.GetAllVaultFolders();
                _allRemovableDrives = _usbDlp.GetConnectedRemovableDrives();
                var settings = _folderLockService.GetSettings();

                // 1. Update Telemetry Cards
                int lockedCount = _allVaultFolders.Count(f => f.IsLocked);
                int unlockedCount = _allVaultFolders.Count(f => !f.IsLocked);
                long totalBytes = _allVaultFolders.Sum(f => f.TotalSizeBytes);
                int totalFiles = _allVaultFolders.Sum(f => f.FileCount);

                TxtVaultCount.Text = $"{_allVaultFolders.Count} Vault{(_allVaultFolders.Count == 1 ? "" : "s")}";
                TxtVaultCountSub.Text = $"{lockedCount} Locked | {unlockedCount} Unlocked";

                string sizeStr = totalBytes > 1024L * 1024L * 1024L
                    ? $"{totalBytes / (1024.0 * 1024.0 * 1024.0):F2} GB"
                    : $"{totalBytes / (1024.0 * 1024.0):F1} MB";
                TxtVaultTotalSize.Text = sizeStr;
                TxtVaultFilesSub.Text = $"{totalFiles:N0} Total Files Protected";

                bool usbProtected = _usbDlp.IsUsbWriteProtectActive();
                if (usbProtected)
                {
                    TxtVaultUsbStatus.Text = "🔒 Read-Only (Write-Blocked)";
                    TxtVaultUsbStatus.Foreground = (Brush)FindResource("AccentGlowBrush");
                    TxtVaultUsbSub.Text = "External copying barred";
                    if (BtnToggleUsbWriteBlock != null)
                    {
                        BtnToggleUsbWriteBlock.Content = "🔓 Disable USB Write-Block";
                        BtnToggleUsbWriteBlock.Style = (Style)FindResource("VortexSecondaryButton");
                    }
                    if (TxtUsbPolicyBadge != null)
                    {
                        TxtUsbPolicyBadge.Text = "STATUS: 🔒 PROTECTED (READ-ONLY)";
                        TxtUsbPolicyBadge.Foreground = (Brush)FindResource("StatusSafeBrush");
                    }
                }
                else
                {
                    TxtVaultUsbStatus.Text = "🔓 Standard (Write Allowed)";
                    TxtVaultUsbStatus.Foreground = (Brush)FindResource("TextSecondaryBrush");
                    TxtVaultUsbSub.Text = "Click to enforce Read-Only";
                    if (BtnToggleUsbWriteBlock != null)
                    {
                        BtnToggleUsbWriteBlock.Content = "🔒 Enable USB Write-Block";
                        BtnToggleUsbWriteBlock.Style = (Style)FindResource("VortexPrimaryButton");
                    }
                    if (TxtUsbPolicyBadge != null)
                    {
                        TxtUsbPolicyBadge.Text = "STATUS: 🔓 UNPROTECTED (WRITES ALLOWED)";
                        TxtUsbPolicyBadge.Foreground = (Brush)FindResource("StatusWarningBrush");
                    }
                }

                if (settings.UsbAllowedUntil.HasValue && settings.UsbAllowedUntil.Value > DateTime.UtcNow)
                {
                    if (BtnLockUsbNow != null) BtnLockUsbNow.Visibility = Visibility.Visible;
                }
                else
                {
                    if (BtnLockUsbNow != null) BtnLockUsbNow.Visibility = Visibility.Collapsed;
                    if (TxtUsbCountdown != null)
                    {
                        TxtUsbCountdown.Text = "Need to copy legitimate files to a USB? Authorize a timed write window with your Master Password.";
                    }
                }

                if (settings.AutoLockOnScreenLock)
                {
                    TxtVaultAutoLockStatus.Text = settings.AutoLockIdleMinutes > 0 ? $"🛡️ Active (Win+L / {settings.AutoLockIdleMinutes}m)" : "🛡️ Active (Win+L)";
                    TxtVaultAutoLockStatus.Foreground = (Brush)FindResource("StatusSafeBrush");
                }
                else
                {
                    TxtVaultAutoLockStatus.Text = "⚠️ Screen Lock Disabled";
                    TxtVaultAutoLockStatus.Foreground = (Brush)FindResource("StatusWarningBrush");
                }

                // SubTab Badge count
                if (SubTabVaultFolders != null)
                {
                    SubTabVaultFolders.Content = $"📁 Protected Folders & Vaults ({_allVaultFolders.Count})";
                }

                // Settings controls synchronization
                if (ChkVaultLockOnScreenLock != null) ChkVaultLockOnScreenLock.IsChecked = settings.AutoLockOnScreenLock;
                if (ChkVaultContextMenu != null) ChkVaultContextMenu.IsChecked = ShellContextMenuService.IsContextMenuRegistered();

                ApplyVaultFilter();
                if (LvRemovableDrives != null) LvRemovableDrives.ItemsSource = _allRemovableDrives;
            }
            catch { }
        }

        private void ApplyVaultFilter()
        {
            if (_allVaultFolders == null || LvVaultFolders == null) return;
            string q = TxtVaultFilter?.Text?.Trim().ToLowerInvariant() ?? string.Empty;

            if (string.IsNullOrEmpty(q))
            {
                LvVaultFolders.ItemsSource = _allVaultFolders;
                return;
            }

            LvVaultFolders.ItemsSource = _allVaultFolders.Where(f =>
                f.FolderName.ToLowerInvariant().Contains(q) ||
                f.FolderPath.ToLowerInvariant().Contains(q) ||
                f.ProtectionMode.ToLowerInvariant().Contains(q)).ToList();
        }

        private void OnVaultFilterChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtVaultFilterPlaceholder != null)
            {
                TxtVaultFilterPlaceholder.Visibility = string.IsNullOrEmpty(TxtVaultFilter?.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            ApplyVaultFilter();
        }

        private void OnVaultRefreshClick(object sender, RoutedEventArgs e)
        {
            RefreshVaultView();
            TxtStatusBar.Text = "Vault and USB device list refreshed.";
        }

        private bool EnsureMasterSecurityConfigured()
        {
            var settings = _folderLockService.GetSettings();
            if (!settings.IsSetup)
            {
                var setupWin = new VaultSetupWindow(_folderLockService, _vaultCrypto) { Owner = this };
                bool? res = setupWin.ShowDialog();
                if (res == true && setupWin.IsSetupComplete)
                {
                    VortexDialogWindow.ShowSuccess(this, "Master Security Configured", "Zero-Trust Active", "Vault Ready",
                        "Master Password and Emergency Recovery Key configured successfully.");
                    RefreshVaultView();
                    return true;
                }
                return false;
            }
            return true;
        }

        private void OnVaultSetupMasterClick(object sender, RoutedEventArgs e)
        {
            if (!EnsureMasterSecurityConfigured()) return;

            var (auth, _) = VaultPasswordDialog.Prompt(this, _folderLockService, "Master Security Verification", "Enter your current Master Password to manage security settings.");
            if (auth)
            {
                SubTabVaultSettings.IsChecked = true;
            }
        }

        private void OnVaultLockAllClick(object sender, RoutedEventArgs e)
        {
            _folderLockService.LockAllVaults("User 1-Click Master Lock");
            VortexDialogWindow.ShowSuccess(this, "All Vaults Sealed", "100% Locked", "Stealth & Encryption Active",
                "All protected directories have been encrypted and locked. Files are completely inaccessible until unlocked.");
            RefreshVaultView();
        }

        private void OnVaultAddFolderClick(object sender, RoutedEventArgs e)
        {
            if (!EnsureMasterSecurityConfigured()) return;

            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Directory to Protect & Lock with Vortex Vault",
                Multiselect = false
            };

            if (dlg.ShowDialog(this) == true)
            {
                string path = dlg.FolderName;
                var (valid, reason) = _folderLockService.ValidateFolderPath(path);
                if (!valid)
                {
                    VortexDialogWindow.ShowWarning(this, "Cannot Protect Folder", reason);
                    return;
                }

                // Ask for mode: Stealth (Invisible) or Visible (Locked with barrier)
                var result = MessageBox.Show(
                    $"Would you like to lock '{Path.GetFileName(path)}' in Stealth (Invisible) Mode?\n\n" +
                    "• YES = Stealth Mode (Folder completely vanishes from Explorer & CMD until unlocked)\n" +
                    "• NO = Visible Mode (Folder remains visible, but all files are encrypted & access is denied)",
                    "Choose Protection Mode", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel) return;
                bool isStealth = (result == MessageBoxResult.Yes);

                var (auth, password) = VaultPasswordDialog.Prompt(this, _folderLockService, "Lock Folder Authentication", $"Enter Master Password to encrypt and lock '{Path.GetFileName(path)}'.");
                if (auth)
                {
                    TxtStatusBar.Text = $"Encrypting and locking '{Path.GetFileName(path)}' with AES-256-GCM...";
                    var (success, msg) = _folderLockService.ProtectFolder(path, password, isStealth);
                    if (success)
                    {
                        VortexDialogWindow.ShowSuccess(this, "Folder Protected in Vault", Path.GetFileName(path), isStealth ? "Stealth Mode" : "Visible Locked",
                            $"Directory encrypted and sealed with AES-256-GCM.\n• Path: {path}\n• Mode: {(isStealth ? "Invisible in Explorer/CMD" : "Visible but impassable")}");
                        RefreshVaultView();
                    }
                    else
                    {
                        VortexDialogWindow.ShowWarning(this, "Lock Error", msg);
                    }
                }
            }
        }

        private void OnVaultToggleLockRowClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is VaultFolderModel item)
            {
                if (item.IsLocked)
                {
                    // Prompt password to unlock
                    var (auth, password) = VaultPasswordDialog.Prompt(this, _folderLockService, "Unlock Folder", $"Enter Master Password to decrypt and unlock '{item.FolderName}'.");
                    if (auth)
                    {
                        TxtStatusBar.Text = $"Decrypting and restoring '{item.FolderName}'...";
                        var (success, msg) = _folderLockService.UnlockFolder(item.FolderPath, password, openInExplorer: true);
                        if (!success) VortexDialogWindow.ShowWarning(this, "Unlock Failed", msg);
                        RefreshVaultView();
                    }
                }
                else
                {
                    // Prompt password to lock
                    var (auth, password) = VaultPasswordDialog.Prompt(this, _folderLockService, "Lock Folder", $"Enter Master Password to re-encrypt and lock '{item.FolderName}'.");
                    if (auth)
                    {
                        TxtStatusBar.Text = $"Re-encrypting and locking '{item.FolderName}'...";
                        var (success, msg) = _folderLockService.LockFolder(item.FolderPath, password);
                        if (!success) VortexDialogWindow.ShowWarning(this, "Lock Failed", msg);
                        RefreshVaultView();
                    }
                }
            }
        }

        private void OnVaultToggleStealthRowClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is VaultFolderModel item)
            {
                var (auth, password) = VaultPasswordDialog.Prompt(this, _folderLockService, "Switch Protection Mode", $"Enter Master Password to switch '{item.FolderName}' between Stealth and Visible mode.");
                if (auth)
                {
                    var (success, msg) = _folderLockService.SetStealthMode(item.FolderPath, !item.IsStealth, password);
                    if (success)
                    {
                        RefreshVaultView();
                        TxtStatusBar.Text = msg;
                    }
                    else
                    {
                        VortexDialogWindow.ShowWarning(this, "Vault Error", msg);
                    }
                }
            }
        }

        private void OnLockedFolderAttemptDetected(VaultFolderModel vault)
        {
            try
            {
                bool wasVisible = IsVisible && WindowState != WindowState.Minimized;

                // Prompt directly with topmost modal dialog without maximizing whole application dashboard
                var (auth, password) = VaultPasswordDialog.Prompt(
                    wasVisible ? this : null,
                    _folderLockService,
                    "Protected Folder Locked",
                    $"'{vault.FolderName}' is currently locked with Vortex Shield.\nEnter your Master Password to decrypt and access:");

                if (auth)
                {
                    var (success, msg) = _folderLockService.UnlockFolder(vault.FolderPath, password, openInExplorer: true);
                    if (success)
                    {
                        if (wasVisible)
                        {
                            RefreshVaultView();
                            TxtStatusBar.Text = $"'{vault.FolderName}' unlocked successfully.";
                        }
                    }
                }
            }
            catch { }
        }

        private void OnVaultOpenFolderRowClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is VaultFolderModel item)
            {
                if (item.IsLocked)
                {
                    var (auth, password) = VaultPasswordDialog.Prompt(this, _folderLockService, "Unlock & Open Folder", $"Enter Master Password to unlock '{item.FolderName}' and view in File Explorer.");
                    if (auth)
                    {
                        _folderLockService.UnlockFolder(item.FolderPath, password, openInExplorer: true);
                        RefreshVaultView();
                    }
                }
                else
                {
                    if (Directory.Exists(item.FolderPath))
                    {
                        try { System.Diagnostics.Process.Start("explorer.exe", item.FolderPath); } catch { }
                    }
                }
            }
        }

        private void OnVaultRemoveRowClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is VaultFolderModel item)
            {
                bool confirm = VortexDialogWindow.ShowConfirm(this, "Remove from Vault",
                    $"Remove '{item.FolderName}' from Vault protection?\n\nIf the folder is currently locked, it will be decrypted and restored to a standard unencrypted folder.",
                    "Remove & Restore");
                if (!confirm) return;

                var (auth, password) = VaultPasswordDialog.Prompt(this, _folderLockService, "Remove from Vault Authentication", $"Enter Master Password to restore '{item.FolderName}'.");
                if (auth)
                {
                    var (success, msg) = _folderLockService.RemoveFromVault(item.FolderPath, password);
                    if (success)
                    {
                        VortexDialogWindow.ShowSuccess(this, "Folder Restored to Normal", item.FolderName, "Protection Removed", msg);
                        RefreshVaultView();
                    }
                    else
                    {
                        VortexDialogWindow.ShowWarning(this, "Remove Error", msg);
                    }
                }
            }
        }

        private void OnToggleUsbWriteBlockClick(object sender, RoutedEventArgs e)
        {
            if (!EnsureMasterSecurityConfigured()) return;

            bool current = _usbDlp.IsUsbWriteProtectActive();
            var (auth, _) = VaultPasswordDialog.Prompt(this, _folderLockService, "USB Security Policy",
                current ? "Enter Master Password to disable USB Write Protection." : "Enter Master Password to enable USB Write-Protection.");

            if (auth)
            {
                var (success, msg) = _usbDlp.SetUsbWriteBlockEnabled(!current);
                if (success)
                {
                    VortexDialogWindow.ShowSuccess(this, "USB Policy Updated", !current ? "Read-Only Enforced" : "Standard Writes", "Storage Security", msg);
                    RefreshVaultView();
                }
                else
                {
                    VortexDialogWindow.ShowWarning(this, "Policy Error", msg);
                }
            }
        }

        private void AuthorizeUsbWindow(int minutes)
        {
            if (!EnsureMasterSecurityConfigured()) return;

            var (auth, _) = VaultPasswordDialog.Prompt(this, _folderLockService, "Authorize USB Write Access",
                $"Enter Master Password to temporarily allow file copying to external drives for {minutes} minutes.");

            if (auth)
            {
                var (success, msg) = _usbDlp.AuthorizeTemporaryWrite(TimeSpan.FromMinutes(minutes));
                if (success)
                {
                    VortexDialogWindow.ShowSuccess(this, "Temporary USB Write Access Granted", $"{minutes} Minutes Active", "Timer Running", msg);
                    RefreshVaultView();
                }
            }
        }

        private void OnAuthorizeUsb5MinClick(object sender, RoutedEventArgs e) => AuthorizeUsbWindow(5);
        private void OnAuthorizeUsb15MinClick(object sender, RoutedEventArgs e) => AuthorizeUsbWindow(15);
        private void OnAuthorizeUsb30MinClick(object sender, RoutedEventArgs e) => AuthorizeUsbWindow(30);

        private void OnLockUsbNowClick(object sender, RoutedEventArgs e)
        {
            _usbDlp.LockUsbNow();
            VortexDialogWindow.ShowSuccess(this, "USB Write Access Revoked", "100% Read-Only", "Storage Locked", "All external drives have been locked back down into Read-Only mode.");
            RefreshVaultView();
        }

        private void OnChangeVaultPasswordClick(object sender, RoutedEventArgs e)
        {
            if (!EnsureMasterSecurityConfigured()) return;

            var setupWin = new VaultSetupWindow(_folderLockService, _vaultCrypto) { Owner = this };
            setupWin.ShowDialog();
            RefreshVaultView();
        }

        private void OnViewRecoveryKeyClick(object sender, RoutedEventArgs e)
        {
            if (!EnsureMasterSecurityConfigured()) return;

            var (auth, _) = VaultPasswordDialog.Prompt(this, _folderLockService, "View Recovery Key", "Enter your Master Password to display your Emergency Recovery Key.");
            if (auth)
            {
                var settings = _folderLockService.GetSettings();
                VortexDialogWindow.ShowSuccess(this, "Emergency Recovery Key Active", "SHA-256 Verified", "Recovery Ready",
                    "Your Emergency Recovery Key is configured. If you need to generate a new key or change credentials, use 'Change Password'.");
            }
        }

        private void OnVaultAutoLockPolicyChanged(object sender, RoutedEventArgs e)
        {
            if (_folderLockService == null || ChkVaultLockOnScreenLock == null) return;
            var settings = _folderLockService.GetSettings();
            settings.AutoLockOnScreenLock = ChkVaultLockOnScreenLock.IsChecked == true;
            _folderLockService.SaveSettings(settings);
            RefreshVaultView();
        }

        private void OnVaultIdleMinutesChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_folderLockService == null || CmbVaultIdleMinutes == null) return;
            if (CmbVaultIdleMinutes.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int mins))
            {
                var settings = _folderLockService.GetSettings();
                settings.AutoLockIdleMinutes = mins;
                _folderLockService.SaveSettings(settings);
                RefreshVaultView();
            }
        }

        private void OnVaultContextMenuPolicyChanged(object sender, RoutedEventArgs e)
        {
            if (_folderLockService == null || ChkVaultContextMenu == null) return;
            bool enable = ChkVaultContextMenu.IsChecked == true;
            if (enable)
            {
                var (success, msg) = ShellContextMenuService.RegisterContextMenu();
                TxtStatusBar.Text = msg;
            }
            else
            {
                var (success, msg) = ShellContextMenuService.UnregisterContextMenu();
                TxtStatusBar.Text = msg;
            }
        }
        #endregion
    }
}