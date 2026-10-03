using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Vortex.Core.Modes;
using Vortex.Core.ProcessManagement;

namespace Vortex.Views
{
    public partial class VortexOrbWindow : Window
    {
        private readonly MemoryOptimizer _optimizer;
        private readonly FocusModeManager _focusMgr;
        private readonly Action _openMainWindowAction;
        private readonly DispatcherTimer _timer;

        public VortexOrbWindow(MemoryOptimizer optimizer, FocusModeManager focusMgr, Action openMainWindowAction)
        {
            InitializeComponent();
            _optimizer = optimizer;
            _focusMgr = focusMgr;
            _openMainWindowAction = openMainWindowAction;

            // Position at top-right corner of screen (compact 106x106 footprint)
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            Left = screenWidth - Width - 30;
            Top = 50;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _timer.Tick += (s, e) => UpdateRamDisplay();
            _timer.Start();

            UpdateRamDisplay();
        }

        private void UpdateRamDisplay()
        {
            var status = _optimizer.GetMemoryStatus();
            TxtRamPercent.Text = $"{status.loadPercent}%";

            double usedGb = (status.totalBytes - status.availBytes) / (1024.0 * 1024.0 * 1024.0);
            double totalGb = status.totalBytes / (1024.0 * 1024.0 * 1024.0);
            ToolTip = $"Vortex Orb • RAM: {status.loadPercent}% ({usedGb:F1} / {totalGb:F0} GB)\nLeft-Click: Turbo Boost\nRight-Click: Menu";

            if (status.loadPercent < 60)
            {
                GlowRing.Stroke = System.Windows.Media.Brushes.SeaGreen;
            }
            else if (status.loadPercent < 80)
            {
                GlowRing.Stroke = System.Windows.Media.Brushes.RoyalBlue;
            }
            else
            {
                GlowRing.Stroke = System.Windows.Media.Brushes.Crimson;
            }
        }

        private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private async void OnOrbClicked(object sender, MouseButtonEventArgs e)
        {
            await TriggerBoostAsync();
        }

        private async void OnMenuTurboBoost(object sender, RoutedEventArgs e)
        {
            await TriggerBoostAsync();
        }

        private async Task TriggerBoostAsync()
        {
            var anim = (Storyboard)FindResource("BoostStoryboard");
            anim.Begin();

            long reclaimed = await Task.Run(() => _optimizer.OptimizeAllWorkingSets());
            UpdateRamDisplay();

            string reclaimedStr = reclaimed > 1024L * 1024L * 1024L
                ? $"{reclaimed / (1024.0 * 1024.0 * 1024.0):F1} GB"
                : $"{reclaimed / (1024.0 * 1024.0):F0} MB";

            TxtToast.Text = $"Cleaned {reclaimedStr}!";
            ShowToast();
        }

        private void ShowToast()
        {
            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromSeconds(0.2));
            var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromSeconds(0.4))
            {
                BeginTime = TimeSpan.FromSeconds(2.0)
            };

            var sb = new Storyboard();
            sb.Children.Add(fadeIn);
            sb.Children.Add(fadeOut);
            Storyboard.SetTarget(fadeIn, ToastBubble);
            Storyboard.SetTargetProperty(fadeIn, new PropertyPath("Opacity"));
            Storyboard.SetTarget(fadeOut, ToastBubble);
            Storyboard.SetTargetProperty(fadeOut, new PropertyPath("Opacity"));
            sb.Begin();
        }

        private void OnMenuToggleFocus(object sender, RoutedEventArgs e)
        {
            if (_focusMgr.IsFocusModeActive)
            {
                string msg = _focusMgr.DisableFocusMode();
                TxtToast.Text = "Focus OFF";
            }
            else
            {
                var (_, msg) = _focusMgr.EnableFocusMode();
                TxtToast.Text = "Focus ON";
            }
            ShowToast();
        }

        private void OnMenuOpenMain(object sender, RoutedEventArgs e)
        {
            _openMainWindowAction?.Invoke();
        }

        private void OnMenuToggleTopmost(object sender, RoutedEventArgs e)
        {
            Topmost = !Topmost;
        }

        private void OnMenuCloseWidget(object sender, RoutedEventArgs e)
        {
            Hide();
        }
    }
}
