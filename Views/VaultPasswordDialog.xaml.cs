using System;
using System.Windows;
using System.Windows.Input;
using Vortex.Core.Vault;

namespace Vortex.Views
{
    public partial class VaultPasswordDialog : Window
    {
        private readonly FolderLockService _lockService;
        private bool _isRecoveryKeyMode = false;

        public string EnteredCredential { get; private set; } = string.Empty;
        public bool IsAuthenticated { get; private set; } = false;

        public VaultPasswordDialog(FolderLockService lockService, string title = "Authentication Required", string prompt = "Enter your Master Password to proceed.")
        {
            InitializeComponent();
            _lockService = lockService;

            TxtTitle.Text = title;
            TxtPrompt.Text = prompt;

            Loaded += (s, e) =>
            {
                PbPassword.Focus();
            };
        }

        private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void OnToggleRecoveryKeyClick(object sender, RoutedEventArgs e)
        {
            _isRecoveryKeyMode = !_isRecoveryKeyMode;

            if (_isRecoveryKeyMode)
            {
                GridPasswordBox.Visibility = Visibility.Collapsed;
                GridRecoveryKey.Visibility = Visibility.Visible;
                BtnUseRecoveryKey.Content = "🔒 Use Password";
                TxtPrompt.Text = "Enter your 24-character Emergency Recovery Key (e.g. VRTX-XXXX-XXXX-XXXX-XXXX-XXXX).";
                TxtRecoveryKey.Focus();
            }
            else
            {
                GridPasswordBox.Visibility = Visibility.Visible;
                GridRecoveryKey.Visibility = Visibility.Collapsed;
                BtnUseRecoveryKey.Content = "🔑 Use Recovery Key";
                TxtPrompt.Text = "Enter your Master Password to proceed.";
                PbPassword.Focus();
            }

            TxtError.Visibility = Visibility.Collapsed;
        }

        public bool AllowBypassAuthenticationCheck { get; set; } = false;

        private void OnSubmitClick(object sender, RoutedEventArgs e)
        {
            string credential = _isRecoveryKeyMode
                ? VaultCryptoEngine.FormatRecoveryKey(TxtRecoveryKey.Text)
                : PbPassword.Password;

            if (string.IsNullOrWhiteSpace(credential))
            {
                TxtError.Text = _isRecoveryKeyMode ? "Please enter your Recovery Key." : "Please enter your Master Password.";
                TxtError.Visibility = Visibility.Visible;
                return;
            }

            if (AllowBypassAuthenticationCheck || _lockService.Authenticate(credential))
            {
                EnteredCredential = credential;
                IsAuthenticated = true;
                DialogResult = true;
                Close();
            }
            else
            {
                TxtError.Text = _isRecoveryKeyMode
                    ? "Invalid Recovery Key. Please verify the code and try again."
                    : "Incorrect Master Password. Access Denied.";
                TxtError.Visibility = Visibility.Visible;
                PbPassword.SelectAll();
                PbPassword.Focus();
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public static (bool authenticated, string credential) Prompt(
            Window? owner,
            FolderLockService lockService,
            string title = "Authentication Required",
            string prompt = "Enter your Master Password to proceed.",
            bool allowBypassAuthenticationCheck = false)
        {
            var dlg = new VaultPasswordDialog(lockService, title, prompt)
            {
                AllowBypassAuthenticationCheck = allowBypassAuthenticationCheck
            };
            if (owner != null && owner.IsVisible)
            {
                dlg.Owner = owner;
            }
            else
            {
                dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                dlg.Topmost = true;
                dlg.ShowInTaskbar = true;
            }
            bool? res = dlg.ShowDialog();
            return (res == true && dlg.IsAuthenticated, dlg.EnteredCredential);
        }
    }
}
