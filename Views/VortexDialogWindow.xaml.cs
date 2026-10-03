using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Vortex.Views
{
    public partial class VortexDialogWindow : Window
    {
        public bool Confirmed { get; private set; } = false;

        public VortexDialogWindow()
        {
            InitializeComponent();
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
            Confirmed = false;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            Close();
        }

        public static void ShowTurboBoostSuccess(Window? owner, string reclaimedStr)
        {
            var dlg = new VortexDialogWindow();
            if (owner != null && owner.IsVisible)
            {
                dlg.Owner = owner;
            }
            dlg.TxtCategory.Text = "MEMORY ACCELERATION";
            dlg.TxtTitle.Text = "Turbo Boost Complete";
            dlg.TxtSubtitle.Text = "Working sets trimmed & memory released";
            dlg.TxtIcon.Text = "⚡";
            dlg.BorderIcon.Background = new SolidColorBrush(Color.FromArgb(0x28, 0x00, 0xE5, 0x99));
            dlg.BorderIcon.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0x99));
            dlg.TxtIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0x99));

            dlg.TxtMetricValue.Text = $"+ {reclaimedStr}";
            dlg.TxtMetricValue.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0x99));
            dlg.TxtMetricLabel.Text = "Physical Memory Reclaimed";
            dlg.TxtMetricSubtext.Text = "Flushed dormant allocations into standby pool";

            dlg.TxtMessage.Text = "Vortex successfully flushed dormant memory across all background applications and trimmed working sets. Your system is primed for peak responsiveness.";
            dlg.BtnOk.Content = "Awesome";
            dlg.ShowDialog();
        }

        public static void ShowSuccess(Window? owner, string title, string metricValue, string metricLabel, string message)
        {
            var dlg = new VortexDialogWindow();
            if (owner != null && owner.IsVisible)
            {
                dlg.Owner = owner;
            }
            dlg.TxtCategory.Text = "OPERATION SUCCESS";
            dlg.TxtTitle.Text = title;
            dlg.TxtSubtitle.Text = "Task completed with zero system warnings";
            dlg.TxtIcon.Text = "✓";
            dlg.BorderIcon.Background = new SolidColorBrush(Color.FromArgb(0x28, 0x00, 0xE5, 0x99));
            dlg.BorderIcon.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0x99));
            dlg.TxtIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0x99));

            if (!string.IsNullOrEmpty(metricValue))
            {
                dlg.TxtMetricValue.Text = metricValue;
                dlg.TxtMetricLabel.Text = metricLabel;
                dlg.TxtMetricSubtext.Text = "Verified & safely executed";
            }
            else
            {
                dlg.BorderMetricCard.Visibility = Visibility.Collapsed;
            }

            dlg.TxtMessage.Text = message;
            dlg.BtnOk.Content = "Got It";
            dlg.ShowDialog();
        }

        public static void ShowWarning(Window? owner, string title, string message)
        {
            var dlg = new VortexDialogWindow();
            if (owner != null && owner.IsVisible)
            {
                dlg.Owner = owner;
            }
            dlg.TxtCategory.Text = "SAFETY PROTOCOL";
            dlg.TxtTitle.Text = title;
            dlg.TxtSubtitle.Text = "Action restricted by protection heuristics";
            dlg.TxtIcon.Text = "🛡️";
            dlg.BorderIcon.Background = new SolidColorBrush(Color.FromArgb(0x28, 0xF5, 0x9E, 0x0B));
            dlg.BorderIcon.BorderBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
            dlg.TxtIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));

            dlg.BorderMetricCard.Visibility = Visibility.Collapsed;
            dlg.TxtMessage.Text = message;
            dlg.BtnOk.Content = "Understood";
            dlg.ShowDialog();
        }

        public static bool ShowConfirm(Window? owner, string title, string prompt, string confirmAction = "Proceed")
        {
            var dlg = new VortexDialogWindow();
            if (owner != null && owner.IsVisible)
            {
                dlg.Owner = owner;
            }
            dlg.TxtCategory.Text = "CONFIRMATION REQUIRED";
            dlg.TxtTitle.Text = title;
            dlg.TxtSubtitle.Text = "Please review before continuing";
            dlg.TxtIcon.Text = "⚠️";
            dlg.BorderIcon.Background = new SolidColorBrush(Color.FromArgb(0x28, 0x6D, 0x4A, 0xFF));
            dlg.BorderIcon.BorderBrush = new SolidColorBrush(Color.FromRgb(0x6D, 0x4A, 0xFF));
            dlg.TxtIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x6D, 0x4A, 0xFF));

            dlg.BorderMetricCard.Visibility = Visibility.Collapsed;
            dlg.TxtMessage.Text = prompt;
            dlg.BtnCancel.Visibility = Visibility.Visible;
            dlg.BtnOk.Content = confirmAction;
            dlg.ShowDialog();
            return dlg.Confirmed;
        }
    }
}
