using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using VortexInstaller.Services;

namespace VortexInstaller
{
    public partial class MainWindow : Window
    {
        private readonly InstallService _installService;
        private InstallOptions _options;

        public MainWindow()
        {
            InitializeComponent();
            _installService = new InstallService();
            _options = new InstallOptions();

            TxtInstallPath.Text = _options.TargetDirectory;
            _installService.ProgressChanged += OnProgressChanged;
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

        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new OpenFolderDialog
                {
                    Title = "Select Vortex Installation Directory",
                    InitialDirectory = _options.TargetDirectory
                };

                if (dialog.ShowDialog() == true)
                {
                    string selected = dialog.FolderName;
                    if (!selected.EndsWith("Vortex", StringComparison.OrdinalIgnoreCase))
                    {
                        selected = System.IO.Path.Combine(selected, "Vortex");
                    }
                    TxtInstallPath.Text = selected;
                }
            }
            catch
            {
                // Fallback if OpenFolderDialog is unsupported in environment
            }
        }

        private async void OnStartInstallClick(object sender, RoutedEventArgs e)
        {
            _options.TargetDirectory = TxtInstallPath.Text.Trim();
            _options.CreateDesktopShortcut = ChkDesktopShortcut.IsChecked ?? true;
            _options.CreateStartMenuShortcut = ChkStartMenu.IsChecked ?? true;
            _options.RunOnStartup = ChkStartup.IsChecked ?? true;

            StepWelcome.Visibility = Visibility.Collapsed;
            StepInstalling.Visibility = Visibility.Visible;

            InstallResult result = await _installService.InstallAsync(_options);

            if (result.Success)
            {
                await Task.Delay(400);
                StepInstalling.Visibility = Visibility.Collapsed;
                StepComplete.Visibility = Visibility.Visible;
            }
            else
            {
                string msg = !string.IsNullOrEmpty(result.ErrorMessage) 
                    ? $"Installation encountered an error:\n\n{result.ErrorMessage}\n\nPlease ensure you have permission to write to the destination directory and that Vortex is not locked."
                    : "Installation encountered an error. Please ensure you have permission to write to the destination directory.";

                MessageBox.Show(msg, "Installation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StepInstalling.Visibility = Visibility.Collapsed;
                StepWelcome.Visibility = Visibility.Visible;
            }
        }

        private void OnProgressChanged(object? sender, InstallProgressEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                TxtProgressPercent.Text = $"{e.Percent}%";
                TxtInstallStatus.Text = e.StatusMessage;

                double maxBarWidth = 478;
                ProgressBarFill.Width = Math.Max(0, Math.Min(maxBarWidth, (e.Percent / 100.0) * maxBarWidth));
            });
        }

        private void OnFinishClick(object sender, RoutedEventArgs e)
        {
            if (ChkLaunchNow.IsChecked == true)
            {
                string exePath = Path.Combine(_options.TargetDirectory, "Vortex.exe");
                if (File.Exists(exePath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = exePath,
                            WorkingDirectory = _options.TargetDirectory,
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }
            }

            Close();
        }

        private void OnOpenFolderClick(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(_options.TargetDirectory))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _options.TargetDirectory,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }
    }
}
