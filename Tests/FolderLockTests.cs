using System;
using System.IO;
using Vortex.Core.Database;
using Vortex.Core.Vault;

namespace Vortex.Tests
{
    public static class FolderLockTests
    {
        public static void RunAll()
        {
            Test_BlacklistedSystemPaths();
            Test_ValidUserPaths();
        }

        private static void Test_BlacklistedSystemPaths()
        {
            var db = new DatabaseService();
            var crypto = new VaultCryptoEngine();
            var usb = new UsbDlpService(db);
            var service = new FolderLockService(db, crypto, usb);

            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var (validWin, _) = service.ValidateFolderPath(winDir);
            if (validWin)
                throw new Exception($"Validation should fail for Windows directory: {winDir}");

            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var (validProg, _) = service.ValidateFolderPath(progFiles);
            if (validProg)
                throw new Exception($"Validation should fail for Program Files: {progFiles}");

            string rootDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var (validRoot, _) = service.ValidateFolderPath(rootDrive);
            if (validRoot)
                throw new Exception($"Validation should fail for root drive: {rootDrive}");

            TestRunner.Pass("FolderLock: Windows Critical System Directories Blacklist Enforcement");
        }

        private static void Test_ValidUserPaths()
        {
            var db = new DatabaseService();
            var crypto = new VaultCryptoEngine();
            var usb = new UsbDlpService(db);
            var service = new FolderLockService(db, crypto, usb);

            string userFolder = Path.Combine(Path.GetTempPath(), "VortexUserFolder_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(userFolder);

            try
            {
                var (valid, reason) = service.ValidateFolderPath(userFolder);
                if (!valid)
                    throw new Exception($"Validation failed for legitimate user directory: {reason}");

                TestRunner.Pass("FolderLock: Legitimate Custom Directory Path Validation");
            }
            finally
            {
                if (Directory.Exists(userFolder)) Directory.Delete(userFolder);
            }
        }
    }
}
