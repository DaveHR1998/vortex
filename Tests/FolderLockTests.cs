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
            Test_RecoveryKey_Synchronization_And_Normalization();
            Test_SelfContained_V2_Standalone_Rescue_Without_DB();
            Test_LockAll_SessionKey_DataPreservation();
            Test_InPlace_Container_Portability_And_Reimport();
            Test_PasswordChange_And_DesyncRecovery_Cycle();
        }

        private static void Test_RecoveryKey_Synchronization_And_Normalization()
        {
            string dbPath = Path.Combine(Path.GetTempPath(), "VortexTest_DB_Sync_" + Guid.NewGuid().ToString("N") + ".db");
            var db = new DatabaseService(dbPath);
            var crypto = new VaultCryptoEngine();
            var usb = new UsbDlpService(db);
            var service = new FolderLockService(db, crypto, usb);

            var (previewKey, _) = crypto.GenerateRecoveryKey();
            string password = "TestMasterPassword123!";

            var (setupOk, savedKey, _) = service.SetupMasterSecurity(password, previewKey);
            if (!setupOk || savedKey != previewKey)
                throw new Exception("SetupMasterSecurity failed to synchronize recovery key!");

            // Test authentication with exact key
            if (!service.Authenticate(previewKey))
                throw new Exception("Authentication failed with exact Recovery Key!");

            // Test authentication with lowercase key
            if (!service.Authenticate(previewKey.ToLowerInvariant()))
                throw new Exception("Authentication failed with lowercase Recovery Key!");

            // Test authentication without hyphens
            if (!service.Authenticate(previewKey.Replace("-", "")))
                throw new Exception("Authentication failed with unhyphenated Recovery Key!");

            // Test authentication without VRTX- prefix
            if (!service.Authenticate(previewKey.Replace("VRTX-", "")))
                throw new Exception("Authentication failed with un-prefixed Recovery Key!");

            // Test authentication with password
            if (!service.Authenticate(password))
                throw new Exception("Authentication failed with Master Password!");

            try { File.Delete(dbPath); } catch { }
            TestRunner.Pass("FolderLock: Recovery Key Synchronization & Format-Agnostic Normalization");
        }

        private static void Test_SelfContained_V2_Standalone_Rescue_Without_DB()
        {
            string dbPath = Path.Combine(Path.GetTempPath(), "VortexTest_DB_Rescue_" + Guid.NewGuid().ToString("N") + ".db");
            var db = new DatabaseService(dbPath);
            var crypto = new VaultCryptoEngine();
            var usb = new UsbDlpService(db);
            var service = new FolderLockService(db, crypto, usb);

            var (plainKey, _) = crypto.GenerateRecoveryKey();
            string password = "RescuePassword999!";
            service.SetupMasterSecurity(password, plainKey);

            string userFolder = Path.Combine(Path.GetTempPath(), "VortexRescueSrc_" + Guid.NewGuid().ToString("N"));
            string rescueFolder1 = Path.Combine(Path.GetTempPath(), "VortexRescueDst1_" + Guid.NewGuid().ToString("N"));
            string rescueFolder2 = Path.Combine(Path.GetTempPath(), "VortexRescueDst2_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(userFolder);

            try
            {
                string secretDoc = Path.Combine(userFolder, "confidential.txt");
                File.WriteAllText(secretDoc, "Highly Classified Financial Data 2026");

                var (pSuccess, pMsg) = service.ProtectFolder(userFolder, password, isStealth: false);
                if (!pSuccess) throw new Exception($"ProtectFolder failed: {pMsg}");

                var model = service.GetAllVaultFolders().First(f => f.FolderPath.Equals(userFolder, StringComparison.OrdinalIgnoreCase));
                string containerPath = model.EncryptedPayloadPath;

                // Test Standalone Rescue using Password with completely standalone crypto engine (NO DATABASE)
                var standaloneEngine = new VaultCryptoEngine();
                var (decPassOk, decPassErr) = standaloneEngine.DecryptVaultToDirectory(containerPath, rescueFolder1, password);
                if (!decPassOk) throw new Exception($"Standalone rescue with password failed: {decPassErr}");

                string restoredDoc1 = Path.Combine(rescueFolder1, "confidential.txt");
                if (!File.Exists(restoredDoc1) || File.ReadAllText(restoredDoc1) != "Highly Classified Financial Data 2026")
                    throw new Exception("Standalone rescue file content mismatch with password!");

                // Test Standalone Rescue using Emergency Recovery Key with NO DATABASE
                var (decRecOk, decRecErr) = standaloneEngine.DecryptVaultToDirectory(containerPath, rescueFolder2, plainKey);
                if (!decRecOk) throw new Exception($"Standalone rescue with recovery key failed: {decRecErr}");

                string restoredDoc2 = Path.Combine(rescueFolder2, "confidential.txt");
                if (!File.Exists(restoredDoc2) || File.ReadAllText(restoredDoc2) != "Highly Classified Financial Data 2026")
                    throw new Exception("Standalone rescue file content mismatch with recovery key!");

                // Clean up vault container and folder
                service.RemoveFromVault(userFolder, password);

                TestRunner.Pass("FolderLock: Standalone V2 Container Rescue (Zero-DB Portability)");
            }
            finally
            {
                if (Directory.Exists(userFolder)) try { Directory.Delete(userFolder, true); } catch { }
                if (Directory.Exists(rescueFolder1)) try { Directory.Delete(rescueFolder1, true); } catch { }
                if (Directory.Exists(rescueFolder2)) try { Directory.Delete(rescueFolder2, true); } catch { }
                try { File.Delete(dbPath); } catch { }
            }
        }

        private static void Test_LockAll_SessionKey_DataPreservation()
        {
            string dbPath = Path.Combine(Path.GetTempPath(), "VortexTest_DB_LockAll_" + Guid.NewGuid().ToString("N") + ".db");
            var db = new DatabaseService(dbPath);
            var crypto = new VaultCryptoEngine();
            var usb = new UsbDlpService(db);
            var service = new FolderLockService(db, crypto, usb);

            string password = "SessionPassword456!";
            service.SetupMasterSecurity(password);

            string userFolder = Path.Combine(Path.GetTempPath(), "VortexPreserveSrc_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(userFolder);

            try
            {
                string file1 = Path.Combine(userFolder, "original.txt");
                File.WriteAllText(file1, "Original Content V1");

                service.ProtectFolder(userFolder, password, isStealth: false);

                // Unlock folder
                service.UnlockFolder(userFolder, password, openInExplorer: false);

                // User modifies file1 and adds a brand new file2 while unlocked
                File.WriteAllText(file1, "Modified Content V2 (Crucial Update)");
                string file2 = Path.Combine(userFolder, "newly_added.txt");
                File.WriteAllText(file2, "Brand New Unsaved Document Content");

                // Trigger LockAll (simulates Win+L or idle lock)
                service.LockAllVaults("Screen Lock Simulation");

                // Verify folder on disk is now locked
                string lockNote = Path.Combine(userFolder, "🔒_LOCKED_BY_VORTEX_VAULT.txt");
                if (!File.Exists(lockNote)) throw new Exception("Folder should contain lock note after LockAll!");
                if (File.Exists(file2)) throw new Exception("file2 should not be clear on disk while locked!");

                // Unlock folder again
                var (unlocked, uMsg) = service.UnlockFolder(userFolder, password, openInExplorer: false);
                if (!unlocked) throw new Exception($"Failed to unlock after LockAll: {uMsg}");

                // CRITICAL ASSERTION: Newly added file and edits must be intact!
                if (!File.Exists(file1) || File.ReadAllText(file1) != "Modified Content V2 (Crucial Update)")
                    throw new Exception("Edited file content was corrupted or lost during LockAll!");

                if (!File.Exists(file2) || File.ReadAllText(file2) != "Brand New Unsaved Document Content")
                    throw new Exception("Newly added file was wiped or lost during LockAll!");

                service.RemoveFromVault(userFolder, password);

                TestRunner.Pass("FolderLock: LockAll Auto-Lock Active Session Re-Encryption & Data Preservation");
            }
            finally
            {
                if (Directory.Exists(userFolder)) try { Directory.Delete(userFolder, true); } catch { }
                try { File.Delete(dbPath); } catch { }
            }
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

                // In visible mode, disk should contain lock note AND in-place .vortexvault container
                string lockNote = Path.Combine(userFolder, "🔒_LOCKED_BY_VORTEX_VAULT.txt");
                string expectedContainer = Path.Combine(userFolder, $"{Path.GetFileName(userFolder)}.vortexvault");
                if (!File.Exists(lockNote)) throw new Exception("Visible locked folder should contain lock note");
                if (!File.Exists(expectedContainer)) throw new Exception("Visible locked folder should contain in-place .vortexvault container!");
                if (File.Exists(file1)) throw new Exception("Clear document.txt should not be present while locked");

                // 2. Switch to Stealth Mode (while locked)
                var (s1Success, s1Msg) = service.SetStealthMode(userFolder, isStealth: true, password);
                if (!s1Success) throw new Exception($"SetStealthMode(true) failed: {s1Msg}");
                if (File.Exists(lockNote)) throw new Exception("Stealth locked folder should NOT have lock note");

                // 3. Switch back to Visible Mode (while locked)
                var (s2Success, s2Msg) = service.SetStealthMode(userFolder, isStealth: false, password);
                if (!s2Success) throw new Exception($"SetStealthMode(false) failed: {s2Msg}");
                if (!File.Exists(lockNote)) throw new Exception("Visible locked folder should have lock note restored");
                if (!File.Exists(expectedContainer)) throw new Exception("Visible locked folder should have in-place container intact");

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

        private static void Test_InPlace_Container_Portability_And_Reimport()
        {
            string dbPath1 = Path.Combine(Path.GetTempPath(), "VortexTest_DB_Port1_" + Guid.NewGuid().ToString("N") + ".db");
            var db1 = new DatabaseService(dbPath1);
            var crypto = new VaultCryptoEngine();
            var usb = new UsbDlpService(db1);
            var service1 = new FolderLockService(db1, crypto, usb);

            string password = "MasterPortabilityPassword!";
            service1.SetupMasterSecurity(password);

            string originalFolder = Path.Combine(Path.GetTempPath(), "VortexPortFolder_" + Guid.NewGuid().ToString("N"));
            string movedFolder = Path.Combine(Path.GetTempPath(), "VortexSimulatedUsb_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(originalFolder);

            try
            {
                string importantDoc = Path.Combine(originalFolder, "my_plans.docx");
                File.WriteAllText(importantDoc, "Confidential Architectural Blueprint 2026");

                // 1. Lock folder in-place
                var (pSuccess, pMsg) = service1.ProtectFolder(originalFolder, password, isStealth: false);
                if (!pSuccess) throw new Exception($"ProtectFolder failed: {pMsg}");

                string containerFile = Path.Combine(originalFolder, $"{Path.GetFileName(originalFolder)}.vortexvault");
                if (!File.Exists(containerFile))
                    throw new Exception("In-place container file was not created inside the folder!");

                // 2. Simulate moving the locked folder to an external USB or new PC location
                Directory.Move(originalFolder, movedFolder);

                // 3. Simulate a fresh app install (new empty database)
                string dbPath2 = Path.Combine(Path.GetTempPath(), "VortexTest_DB_Port2_" + Guid.NewGuid().ToString("N") + ".db");
                var db2 = new DatabaseService(dbPath2);
                var service2 = new FolderLockService(db2, crypto, new UsbDlpService(db2));
                service2.SetupMasterSecurity(password);

                // 4. Re-import the moved folder
                var (reimportSuccess, reimportMsg) = service2.ProtectFolder(movedFolder, password, isStealth: false);
                if (!reimportSuccess) throw new Exception($"Re-import failed: {reimportMsg}");

                // 5. Unlock the imported folder in the new installation
                var (uSuccess, uMsg) = service2.UnlockFolder(movedFolder, password, openInExplorer: false);
                if (!uSuccess) throw new Exception($"Unlock after re-import failed: {uMsg}");

                // 6. Verify data integrity
                string restoredDoc = Path.Combine(movedFolder, "my_plans.docx");
                if (!File.Exists(restoredDoc) || File.ReadAllText(restoredDoc) != "Confidential Architectural Blueprint 2026")
                    throw new Exception("Data was corrupted or lost during portability cycle!");

                service2.RemoveFromVault(movedFolder, password);

                try { File.Delete(dbPath2); } catch { }
                TestRunner.Pass("FolderLock: In-Place Container Portability & Re-import Cycle");
            }
            finally
            {
                if (Directory.Exists(originalFolder)) try { Directory.Delete(originalFolder, true); } catch { }
                if (Directory.Exists(movedFolder)) try { Directory.Delete(movedFolder, true); } catch { }
                try { File.Delete(dbPath1); } catch { }
            }
        }

        private static void Test_PasswordChange_And_DesyncRecovery_Cycle()
        {
            string dbPath = Path.Combine(Path.GetTempPath(), "VortexTest_DB_Recovery_" + Guid.NewGuid().ToString("N") + ".db");
            var db = new DatabaseService(dbPath);
            var crypto = new VaultCryptoEngine();
            var service = new FolderLockService(db, crypto, new UsbDlpService(db));

            string testFolder = Path.Combine(Path.GetTempPath(), "VortexTest_Vault_Rec_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testFolder);

            try
            {
                string originalPass = "InitialSecretPassword123!";
                service.SetupMasterSecurity(originalPass);

                string file1 = Path.Combine(testFolder, "financials.csv");
                File.WriteAllText(file1, "Account,Balance\nChecking,10500\nSavings,45000");

                // 1. Lock folder with original password
                var (pOk, pMsg) = service.ProtectFolder(testFolder, originalPass, isStealth: false);
                if (!pOk) throw new Exception($"ProtectFolder failed: {pMsg}");

                // 2. Safe Password Change to new password
                string changedPass = "UpdatedSecretPassword456!";
                var (cOk, cMsg) = service.ChangeMasterPassword(originalPass, changedPass);
                if (!cOk) throw new Exception($"ChangeMasterPassword failed: {cMsg}");

                // 3. Unlock with changed password
                var (uOk, uMsg) = service.UnlockFolder(testFolder, changedPass, openInExplorer: false);
                if (!uOk) throw new Exception($"Unlock with changed password failed: {uMsg}");
                if (!File.Exists(file1) || !File.ReadAllText(file1).Contains("Checking,10500"))
                    throw new Exception("File content mismatch after password change!");

                // 4. Re-lock with changed password
                service.LockFolder(testFolder, changedPass);

                // 5. Simulate Desync: User sets up a brand new Master Password and generates a new VMK
                string desyncPass = "BrandNewResetPassword789!";
                service.ClearSession();
                // Simulate fresh setup with new random VMK in settings
                byte[] freshVmk = crypto.GenerateVaultMasterKey();
                var (freshHash, freshSalt) = crypto.HashPassword(desyncPass);
                var settings = db.GetVaultSettings();
                settings.PasswordHash = freshHash;
                settings.PasswordSalt = freshSalt;
                settings.WrappedMasterKeyPassword = crypto.WrapMasterKey(freshVmk, desyncPass);
                db.SaveVaultSettings(settings);

                // 6. Normal unlock with desyncPass must fail with key mismatch
                var (failOk, failMsg) = service.UnlockFolder(testFolder, desyncPass, openInExplorer: false);
                if (failOk) throw new Exception("Unlock should have failed due to key mismatch!");

                // 7. Recover and unlock using the previous credential
                var (recOk, recMsg) = service.RecoverAndUnlockFolder(testFolder, changedPass, desyncPass, openInExplorer: false);
                if (!recOk) throw new Exception($"RecoverAndUnlockFolder failed: {recMsg}");

                // 8. Assert data was restored 100%
                if (!File.Exists(file1) || !File.ReadAllText(file1).Contains("Checking,10500"))
                    throw new Exception("File content was lost or corrupted during recovery!");

                // 9. Re-lock and unlock with desyncPass to verify settings are re-synchronized
                var (relockOk, _) = service.LockFolder(testFolder, desyncPass);
                if (!relockOk) throw new Exception("Re-locking after recovery failed!");

                var (syncUnlockOk, syncUnlockMsg) = service.UnlockFolder(testFolder, desyncPass, openInExplorer: false);
                if (!syncUnlockOk) throw new Exception($"Unlock after re-synchronization failed: {syncUnlockMsg}");

                // 10. Clean up folder
                service.RemoveFromVault(testFolder, desyncPass);

                try { File.Delete(dbPath); } catch { }
                TestRunner.Pass("FolderLock: Password Change & Desync Recovery Cycle");
            }
            finally
            {
                if (Directory.Exists(testFolder)) try { Directory.Delete(testFolder, true); } catch { }
                try { File.Delete(dbPath); } catch { }
            }
        }
    }
}
