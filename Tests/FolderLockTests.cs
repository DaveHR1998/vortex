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
            Test_ProtectVisible_SwitchStealth_SwitchVisible_Unlock_DataIntegrity();
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

        private static void Test_ProtectVisible_SwitchStealth_SwitchVisible_Unlock_DataIntegrity()
        {
            var db = new DatabaseService();
            var crypto = new VaultCryptoEngine();
            var usb = new UsbDlpService(db);
            var service = new FolderLockService(db, crypto, usb);

            string password = "TestMasterPassword123!";
            service.SetupMasterSecurity(password);

            string userFolder = Path.Combine(Path.GetTempPath(), "VortexTestVault_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(userFolder);

            try
            {
                string file1 = Path.Combine(userFolder, "document.txt");
                string file2 = Path.Combine(userFolder, "financials.csv");
                File.WriteAllText(file1, "Top Secret Data Content 1");
                File.WriteAllText(file2, "ColA,ColB\n100,200\n300,400");

                // 1. Protect in Visible Mode
                var (pSuccess, pMsg) = service.ProtectFolder(userFolder, password, isStealth: false);
                if (!pSuccess) throw new Exception($"ProtectFolder failed: {pMsg}");

                // In visible mode, disk should only contain lock note
                string lockNote = Path.Combine(userFolder, "🔒_LOCKED_BY_VORTEX_VAULT.txt");
                if (!File.Exists(lockNote)) throw new Exception("Visible locked folder should contain lock note");
                if (File.Exists(file1)) throw new Exception("Clear document.txt should not be present while locked");

                // 2. Switch to Stealth Mode (while locked)
                var (s1Success, s1Msg) = service.SetStealthMode(userFolder, isStealth: true, password);
                if (!s1Success) throw new Exception($"SetStealthMode(true) failed: {s1Msg}");
                if (File.Exists(lockNote)) throw new Exception("Stealth locked folder should NOT have lock note");

                // 3. Switch back to Visible Mode (while locked)
                var (s2Success, s2Msg) = service.SetStealthMode(userFolder, isStealth: false, password);
                if (!s2Success) throw new Exception($"SetStealthMode(false) failed: {s2Msg}");
                if (!File.Exists(lockNote)) throw new Exception("Visible locked folder should have lock note restored");

                // 4. Unlock Folder
                var (uSuccess, uMsg) = service.UnlockFolder(userFolder, password, openInExplorer: false);
                if (!uSuccess) throw new Exception($"UnlockFolder failed: {uMsg}");

                // 5. Verify all user files are completely restored and lock note is removed
                if (!File.Exists(file1) || File.ReadAllText(file1) != "Top Secret Data Content 1")
                    throw new Exception("file1 content corrupted or missing after unlock!");

                if (!File.Exists(file2) || !File.ReadAllText(file2).Contains("300,400"))
                    throw new Exception("file2 content corrupted or missing after unlock!");

                if (File.Exists(lockNote))
                    throw new Exception("Lock note should be completely removed after unlock!");

                // Clean up
                service.RemoveFromVault(userFolder, password);

                TestRunner.Pass("FolderLock: Stealth/Visible Toggling & Container Data Integrity Preservation");
            }
            finally
            {
                if (Directory.Exists(userFolder))
                {
                    try { Directory.Delete(userFolder, true); } catch { }
                }
            }
        }
    }
}
