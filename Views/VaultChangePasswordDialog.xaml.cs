using System;
using System.Windows;
using System.Windows.Input;
using Vortex.Core.Vault;

namespace Vortex.Views
{
    public partial class VaultChangePasswordDialog : Window
    {
        private readonly FolderLockService _lockService;
        private bool _isRecoveryKeyMode = false;

        public bool PasswordChangedSuccessfully { get; private set; } = false;

        public VaultChangePasswordDialog(FolderLockService lockService)
        {
            InitializeComponent();
            _lockService = lockService;

            Loaded += (s, e) => PbCurrentPassword.Focus();
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
                GridCurrentPassword.Visibility = Visibility.Collapsed;
                GridCurrentRecovery.Visibility = Visibility.Visible;
                LblCurrentCred.Text = "CURRENT EMERGENCY RECOVERY KEY";
                BtnUseRecoveryKey.Content = "🔒 Use Current Password";
                TxtCurrentRecoveryKey.Focus();
            }
            else
            {
                GridCurrentPassword.Visibility = Visibility.Visible;
                GridCurrentRecovery.Visibility = Visibility.Collapsed;
                LblCurrentCred.Text = "CURRENT MASTER PASSWORD";
                BtnUseRecoveryKey.Content = "🛡️ Use Recovery Key";
                PbCurrentPassword.Focus();
            }

            TxtError.Visibility = Visibility.Collapsed;
        }

        private void OnUpdateClick(object sender, RoutedEventArgs e)
        {
            string currentCred = _isRecoveryKeyMode
                ? VaultCryptoEngine.FormatRecoveryKey(TxtCurrentRecoveryKey.Text)
                : PbCurrentPassword.Password;

            string newPass = PbNewPassword.Password;
            string confirm = PbConfirmPassword.Password;

            if (string.IsNullOrWhiteSpace(currentCred))
            {
                TxtError.Text = _isRecoveryKeyMode ? "Please enter your Emergency Recovery Key." : "Please enter your current Master Password.";
                TxtError.Visibility = Visibility.Visible;
                return;
            }

            if (string.IsNullOrWhiteSpace(newPass) || newPass.Length < 6)
            {
                TxtError.Text = "New Master Password must be at least 6 characters long.";
                TxtError.Visibility = Visibility.Visible;
                PbNewPassword.Focus();
                return;
            }

            if (newPass != confirm)
            {
                TxtError.Text = "New passwords do not match. Please re-enter.";
                TxtError.Visibility = Visibility.Visible;
                PbConfirmPassword.Focus();
                return;
            }

            var (success, msg) = _lockService.ChangeMasterPassword(currentCred, newPass);
            if (success)
            {
                PasswordChangedSuccessfully = true;
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
