using System;
using System.Windows;
using System.Windows.Input;
using Vortex.Core.Vault;

namespace Vortex.Views
{
    public partial class VaultSetupWindow : Window
    {
        private readonly FolderLockService _lockService;
        private readonly VaultCryptoEngine _crypto;
        private string _generatedRecoveryKey = string.Empty;

        public bool IsSetupComplete { get; private set; } = false;

        public VaultSetupWindow(FolderLockService lockService, VaultCryptoEngine crypto)
        {
            InitializeComponent();
            _lockService = lockService;
            _crypto = crypto;

            // Generate initial recovery key preview
            var (plainKey, _) = _crypto.GenerateRecoveryKey();
            _generatedRecoveryKey = plainKey;
            TxtRecoveryKeyDisplay.Text = plainKey;

            Loaded += (s, e) => PbPassword.Focus();
        }

        private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void OnCopyRecoveryKeyClick(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(_generatedRecoveryKey);
            MessageBox.Show("Emergency Recovery Key copied to clipboard! Please paste it into a password manager or save it securely.",
                "Recovery Key Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnCompleteSetupClick(object sender, RoutedEventArgs e)
        {
            string pass = PbPassword.Password;
            string confirm = PbConfirmPassword.Password;

            if (string.IsNullOrWhiteSpace(pass) || pass.Length < 6)
            {
                TxtError.Text = "Master Password must be at least 6 characters long.";
                TxtError.Visibility = Visibility.Visible;
                PbPassword.Focus();
                return;
            }

            if (pass != confirm)
            {
                TxtError.Text = "Passwords do not match. Please re-enter.";
                TxtError.Visibility = Visibility.Visible;
                PbConfirmPassword.Focus();
                return;
            }

            if (ChkKeySaved.IsChecked != true)
            {
                TxtError.Text = "Please confirm that you have saved your Recovery Key by checking the box.";
                TxtError.Visibility = Visibility.Visible;
                return;
            }

            var (success, key, msg) = _lockService.SetupMasterSecurity(pass);
            if (success)
            {
                IsSetupComplete = true;
                DialogResult = true;
                Close();
            }
            else
            {
                TxtError.Text = msg;
                TxtError.Visibility = Visibility.Visible;
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
