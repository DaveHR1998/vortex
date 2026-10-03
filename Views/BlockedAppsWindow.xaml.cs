using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Vortex.Core.Database;
using Vortex.Core.ProcessManagement;

namespace Vortex.Views
{
    public partial class BlockedAppsWindow : Window
    {
        private readonly ProcessBlockService _blockService;
        private List<DiscoveredAppModel> _allDiscovered = new();
        private List<AppAccessRule> _allAllowed = new();
        private List<ProcessBlockService.BlockedItemModel> _allBlocked = new();

        public BlockedAppsWindow(ProcessBlockService blockService)
        {
            InitializeComponent();
            _blockService = blockService;
            Loaded += async (s, e) => await LoadAllDataAsync();
        }

        private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnTabChanged(object sender, RoutedEventArgs e)
        {
            if (PanelDiscover == null || PanelAllowed == null || PanelBlocked == null) return;

            if (TabDiscover.IsChecked == true)
            {
                PanelDiscover.Visibility = Visibility.Visible;
                PanelAllowed.Visibility = Visibility.Collapsed;
                PanelBlocked.Visibility = Visibility.Collapsed;
            }
            else if (TabAllowed.IsChecked == true)
            {
                PanelDiscover.Visibility = Visibility.Collapsed;
                PanelAllowed.Visibility = Visibility.Visible;
                PanelBlocked.Visibility = Visibility.Collapsed;
            }
            else if (TabBlocked.IsChecked == true)
            {
                PanelDiscover.Visibility = Visibility.Collapsed;
                PanelAllowed.Visibility = Visibility.Collapsed;
                PanelBlocked.Visibility = Visibility.Visible;
            }
        }

        private async Task LoadAllDataAsync()
        {
            TxtStatus.Text = "Scanning installed applications and active PC processes...";
            
            await Task.Run(() =>
            {
                _allDiscovered = _blockService.GetDiscoveredApps();
                _allAllowed = _blockService.GetAllowedApps();
                _allBlocked = _blockService.GetBlockedItems();
            });

            ApplyDiscoverFilter();
            ApplyAllowedFilter();
            ApplyBlockedFilter();
            UpdateCountsAndStatus();
        }

        private void UpdateCountsAndStatus()
        {
            if (TabAllowed != null) TabAllowed.Content = $"🛡️ Allowed Apps ({_allAllowed.Count})";
            if (TabBlocked != null) TabBlocked.Content = $"🚫 Blocked Apps ({_allBlocked.Count})";

            int runningCount = _allDiscovered.Count(a => a.IsRunning);
            TxtStatus.Text = $"Detected {_allDiscovered.Count} PC applications ({runningCount} active right now) | {_allAllowed.Count} allowed, {_allBlocked.Count} blocked.";
        }

        private async void OnRescanClick(object sender, RoutedEventArgs e)
        {
            await LoadAllDataAsync();
        }

        #region TAB 1: Discover & Choose Logic

        private void OnDiscoverFilterChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtDiscoverFilterPlaceholder != null)
            {
                TxtDiscoverFilterPlaceholder.Visibility = string.IsNullOrEmpty(TxtDiscoverFilter?.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            ApplyDiscoverFilter();
        }

        private void ApplyDiscoverFilter()
        {
            if (_allDiscovered == null) return;
            string q = TxtDiscoverFilter?.Text?.Trim().ToLowerInvariant() ?? string.Empty;

            if (string.IsNullOrEmpty(q))
            {
                LvDiscoveredApps.ItemsSource = _allDiscovered;
                return;
            }

            LvDiscoveredApps.ItemsSource = _allDiscovered.Where(a =>
                a.DisplayName.ToLowerInvariant().Contains(q) ||
                a.ProcessName.ToLowerInvariant().Contains(q) ||
                a.Category.ToLowerInvariant().Contains(q) ||
                a.ExecutablePath.ToLowerInvariant().Contains(q)).ToList();
        }

        private void OnAllowDiscoverRowClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is DiscoveredAppModel item)
            {
                var (success, msg) = _blockService.AllowApp(item.ProcessName, item.ExecutablePath, item.DisplayName, item.Category, true);
                
                // Update local model
                item.AccessStatus = "ALLOWED";
                item.MuteNotifications = true;

                RefreshViews();
            }
        }

        private void OnBlockDiscoverRowClick(object sender, RoutedEventArgs e)
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
                    MessageBox.Show($"Cannot create firewall rule: Executable file path for '{item.DisplayName}' could not be located.",
                        "Path Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var (success, msg) = _blockService.BlockFromInternet(item.ProcessName, item.ExecutablePath);
                if (success)
                {
                    item.AccessStatus = "BLOCKED_FIREWALL";
                    RefreshViews();
                }
                else
                {
                    MessageBox.Show(msg, "Quarantine Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OnResetDiscoverRowClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is DiscoveredAppModel item)
            {
                _blockService.ResetAppStatus(item.ProcessName);
                item.AccessStatus = "UNCONFIGURED";
                item.MuteNotifications = false;
                RefreshViews();
            }
        }

        private void OnToggleNotificationDiscoverRowClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is DiscoveredAppModel item)
            {
                _blockService.ToggleNotificationMute(item.ProcessName);
                item.MuteNotifications = !item.MuteNotifications;
                RefreshViews();
            }
        }

        private void OnBrowseAddCustomClick(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Application Executable to Manage",
                Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dlg.ShowDialog(this) == true)
            {
                string path = dlg.FileName;
                string fileName = Path.GetFileName(path);
                string appName = Path.GetFileNameWithoutExtension(path);

                var (success, msg) = _blockService.AllowApp(fileName, path, appName, "User Application", true);
                MessageBox.Show($"'{appName}' is now configured as an Allowed & Trusted application.", "Application Added", MessageBoxButton.OK, MessageBoxImage.Information);

                _allDiscovered = _blockService.GetDiscoveredApps();
                _allAllowed = _blockService.GetAllowedApps();
                _allBlocked = _blockService.GetBlockedItems();
                RefreshViews();
            }
        }

        #endregion

        #region TAB 2: Allowed Apps Logic

        private void OnAllowedFilterChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtAllowedFilterPlaceholder != null)
            {
                TxtAllowedFilterPlaceholder.Visibility = string.IsNullOrEmpty(TxtAllowedFilter?.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            ApplyAllowedFilter();
        }

        private void ApplyAllowedFilter()
        {
            if (_allAllowed == null) return;
            string q = TxtAllowedFilter?.Text?.Trim().ToLowerInvariant() ?? string.Empty;

            if (string.IsNullOrEmpty(q))
            {
                LvAllowedApps.ItemsSource = _allAllowed;
                return;
            }

            LvAllowedApps.ItemsSource = _allAllowed.Where(a =>
                a.DisplayName.ToLowerInvariant().Contains(q) ||
                a.ProcessName.ToLowerInvariant().Contains(q) ||
                a.Category.ToLowerInvariant().Contains(q)).ToList();
        }

        private void OnToggleNotificationAllowedRowClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AppAccessRule item)
            {
                _blockService.ToggleNotificationMute(item.ProcessName);
                _allAllowed = _blockService.GetAllowedApps();
                RefreshViews();
            }
        }

        private void OnRemoveAllowedRowClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AppAccessRule item)
            {
                var result = MessageBox.Show(
                    $"Remove '{item.DisplayName}' ({item.ProcessName}) from the Allowed list?\n\nNetwork monitor heuristics will resume standard inspection for this process.",
                    "Confirm Removal", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _blockService.RevokeAllowApp(item.ProcessName);
                    
                    var match = _allDiscovered.FirstOrDefault(d => d.ProcessName.Equals(item.ProcessName, StringComparison.OrdinalIgnoreCase));
                    if (match != null) match.AccessStatus = "UNCONFIGURED";

                    RefreshViews();
                }
            }
        }

        #endregion

        #region TAB 3: Blocked Apps Logic

        private void OnBlockedFilterChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtBlockedFilterPlaceholder != null)
            {
                TxtBlockedFilterPlaceholder.Visibility = string.IsNullOrEmpty(TxtBlockedFilter?.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            ApplyBlockedFilter();
        }

        private void ApplyBlockedFilter()
        {
            if (_allBlocked == null) return;
            string q = TxtBlockedFilter?.Text?.Trim().ToLowerInvariant() ?? string.Empty;

            if (string.IsNullOrEmpty(q))
            {
                LvBlockedItems.ItemsSource = _allBlocked;
                return;
            }

            LvBlockedItems.ItemsSource = _allBlocked.Where(b =>
                b.ProcessName.ToLowerInvariant().Contains(q) ||
                b.ExecutablePath.ToLowerInvariant().Contains(q)).ToList();
        }

        private void OnUnblockRowClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ProcessBlockService.BlockedItemModel item)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to unblock '{item.ProcessName}'?\n\nType: {item.BlockTypeDisplay}\nPath: {item.ExecutablePath}",
                    "Confirm Unblock", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    if (item.BlockType == "FIREWALL")
                    {
                        var (success, msg) = _blockService.UnblockInternet(item.ProcessName);
                        MessageBox.Show(msg, "Network Restored", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else if (item.BlockType == "EXECUTION")
                    {
                        var (success, msg) = _blockService.UnblockExecution(item.ProcessName);
                        MessageBox.Show(msg, "Execution Restored", MessageBoxButton.OK, MessageBoxImage.Information);
                    }

                    var match = _allDiscovered.FirstOrDefault(d => d.ProcessName.Equals(item.ProcessName, StringComparison.OrdinalIgnoreCase));
                    if (match != null) match.AccessStatus = "UNCONFIGURED";

                    RefreshViews();
                }
            }
        }

        #endregion

        private void RefreshViews()
        {
            _allAllowed = _blockService.GetAllowedApps();
            _allBlocked = _blockService.GetBlockedItems();

            ApplyDiscoverFilter();
            ApplyAllowedFilter();
            ApplyBlockedFilter();
            UpdateCountsAndStatus();
        }
    }
}
