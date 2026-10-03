using System;
using System.Windows;
using Vortex.Core.Database;
using Vortex.Core.Vault;
using Vortex.Views;

namespace Vortex.Tests
{
    public static class WpfStartupTests
    {
        public static void RunAll()
        {
            Test_WpfDialogsInstantiation();
        }

        private static void Test_WpfDialogsInstantiation()
        {
            var db = new DatabaseService();
            var crypto = new VaultCryptoEngine();
            var usb = new UsbDlpService(db);
            var folderLock = new FolderLockService(db, crypto, usb);

            // 1. Test VaultPasswordDialog instantiation and XAML loading
            var pwdDialog = new VaultPasswordDialog(folderLock, "Testing Vault Password Dialog");
            if (pwdDialog == null) throw new Exception("Failed to instantiate VaultPasswordDialog.");

            // 2. Test VaultSetupWindow instantiation and XAML loading
            var setupWindow = new VaultSetupWindow(folderLock, crypto);
            if (setupWindow == null) throw new Exception("Failed to instantiate VaultSetupWindow.");

            TestRunner.Pass("WPF: STA Application Context & Dialog Views Initialized without XAML Errors");
        }
    }
}
