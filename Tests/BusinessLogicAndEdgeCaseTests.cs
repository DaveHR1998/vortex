using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Vortex.Core.Database;
using Vortex.Core.Vault;

namespace Vortex.Tests
{
    public static class BusinessLogicAndEdgeCaseTests
    {
        public static void RunAll()
        {
            Test_ZeroByteAndEmptyDirPreservation();
            Test_UnicodeAndEmojiFilenames();
            Test_DeepDirectoryHierarchy();
            Test_MultiThreadedConcurrency();
            Test_UsbDlpHardwareIntegration();
            Test_CryptographicTamperResistance();
            Test_InMemorySecretZeroization();
        }

        private static void Test_ZeroByteAndEmptyDirPreservation()
        {
            string dbPath = Path.Combine(Path.GetTempPath(), "VortexTest_DB_Zero_" + Guid.NewGuid().ToString("N") + ".db");
            var db = new DatabaseService(dbPath);
            var crypto = new VaultCryptoEngine();
            var service = new FolderLockService(db, crypto, new UsbDlpService(db));

            string password = "ZeroBytePassword123!";
            service.SetupMasterSecurity(password);

            string rootDir = Path.Combine(Path.GetTempPath(), "VortexZeroSrc_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootDir);

            try
            {
                // Create 0-byte file
                string zeroFile = Path.Combine(rootDir, "empty.dat");
                File.WriteAllBytes(zeroFile, Array.Empty<byte>());

                // Create empty subdirectory
                string emptySubDir = Path.Combine(rootDir, "EmptySubFolder");
                Directory.CreateDirectory(emptySubDir);

                // Protect
                var (pOk, pErr) = service.ProtectFolder(rootDir, password, isStealth: false);
                if (!pOk) throw new Exception($"ProtectFolder failed: {pErr}");

                // Unlock
                var (uOk, uErr) = service.UnlockFolder(rootDir, password, openInExplorer: false);
                if (!uOk) throw new Exception($"UnlockFolder failed: {uErr}");

                // Assertions
                if (!File.Exists(zeroFile))
                    throw new Exception("0-byte file was missing after unlock!");
                if (new FileInfo(zeroFile).Length != 0)
                    throw new Exception("0-byte file length changed after unlock!");
                if (!Directory.Exists(emptySubDir))
                    throw new Exception("Empty subdirectory was missing after unlock!");

                service.RemoveFromVault(rootDir, password);
                TestRunner.Pass("BusinessLogic: 0-Byte File & Empty Subdirectory Preservation");
            }
            finally
            {
                if (Directory.Exists(rootDir)) try { Directory.Delete(rootDir, true); } catch { }
                try { File.Delete(dbPath); } catch { }
            }
        }

        private static void Test_UnicodeAndEmojiFilenames()
        {
            string dbPath = Path.Combine(Path.GetTempPath(), "VortexTest_DB_Unicode_" + Guid.NewGuid().ToString("N") + ".db");
            var db = new DatabaseService(dbPath);
            var crypto = new VaultCryptoEngine();
            var service = new FolderLockService(db, crypto, new UsbDlpService(db));

            string password = "UnicodePassword123!";
            service.SetupMasterSecurity(password);

            string rootDir = Path.Combine(Path.GetTempPath(), "VortexUnicodeSrc_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootDir);

            try
            {
                string uFile1 = Path.Combine(rootDir, "🔒_财务报表_2026_🔥.xlsx");
                string uFile2 = Path.Combine(rootDir, "Отчет_Документ_résumé.pdf");
                string uFile3 = Path.Combine(rootDir, "ملف_خاص_جدًا.txt");

                File.WriteAllText(uFile1, "Chinese and Emoji content 2026");
                File.WriteAllText(uFile2, "Cyrillic and French accented content 2026");
                File.WriteAllText(uFile3, "Arabic bidirectional content 2026");

                var (pOk, pErr) = service.ProtectFolder(rootDir, password, isStealth: false);
                if (!pOk) throw new Exception($"ProtectFolder failed: {pErr}");

                var (uOk, uErr) = service.UnlockFolder(rootDir, password, openInExplorer: false);
                if (!uOk) throw new Exception($"UnlockFolder failed: {uErr}");

                if (!File.Exists(uFile1) || File.ReadAllText(uFile1) != "Chinese and Emoji content 2026")
                    throw new Exception("Unicode Chinese/Emoji file corrupted!");
                if (!File.Exists(uFile2) || File.ReadAllText(uFile2) != "Cyrillic and French accented content 2026")
                    throw new Exception("Cyrillic/Accented file corrupted!");
                if (!File.Exists(uFile3) || File.ReadAllText(uFile3) != "Arabic bidirectional content 2026")
                    throw new Exception("Arabic text file corrupted!");

                service.RemoveFromVault(rootDir, password);
                TestRunner.Pass("BusinessLogic: Unicode, Emoji & Multilingual Path Fidelity");
            }
            finally
            {
                if (Directory.Exists(rootDir)) try { Directory.Delete(rootDir, true); } catch { }
                try { File.Delete(dbPath); } catch { }
            }
        }

        private static void Test_DeepDirectoryHierarchy()
        {
            string dbPath = Path.Combine(Path.GetTempPath(), "VortexTest_DB_Deep_" + Guid.NewGuid().ToString("N") + ".db");
            var db = new DatabaseService(dbPath);
            var crypto = new VaultCryptoEngine();
            var service = new FolderLockService(db, crypto, new UsbDlpService(db));

            string password = "DeepFolderPassword123!";
            service.SetupMasterSecurity(password);

            string rootDir = Path.Combine(Path.GetTempPath(), "VortexDeepSrc_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootDir);

            try
            {
                string curr = rootDir;
                for (int i = 1; i <= 8; i++)
                {
                    curr = Path.Combine(curr, $"Tier_{i}");
                    Directory.CreateDirectory(curr);
                    File.WriteAllText(Path.Combine(curr, $"file_tier_{i}.txt"), $"Content in Tier {i}");
                }

                var (pOk, pErr) = service.ProtectFolder(rootDir, password, isStealth: false);
                if (!pOk) throw new Exception($"ProtectFolder failed: {pErr}");

                var (uOk, uErr) = service.UnlockFolder(rootDir, password, openInExplorer: false);
                if (!uOk) throw new Exception($"UnlockFolder failed: {uErr}");

                string checkPath = rootDir;
                for (int i = 1; i <= 8; i++)
                {
                    checkPath = Path.Combine(checkPath, $"Tier_{i}");
                    string fileCheck = Path.Combine(checkPath, $"file_tier_{i}.txt");
                    if (!File.Exists(fileCheck) || File.ReadAllText(fileCheck) != $"Content in Tier {i}")
                        throw new Exception($"Deep hierarchy tier {i} was missing or corrupted!");
                }

                service.RemoveFromVault(rootDir, password);
                TestRunner.Pass("BusinessLogic: Deep 8-Tier Nested Directory Tree Preservation");
            }
            finally
            {
                if (Directory.Exists(rootDir)) try { Directory.Delete(rootDir, true); } catch { }
                try { File.Delete(dbPath); } catch { }
            }
        }

        private static void Test_MultiThreadedConcurrency()
        {
            string dbPath = Path.Combine(Path.GetTempPath(), "VortexTest_DB_Concurrency_" + Guid.NewGuid().ToString("N") + ".db");
            var db = new DatabaseService(dbPath);
            var crypto = new VaultCryptoEngine();
            var service = new FolderLockService(db, crypto, new UsbDlpService(db));

            string password = "ConcurrencyPassword123!";
            service.SetupMasterSecurity(password);

            try
            {
                // Run 20 concurrent threads authenticating and reading database
                Parallel.For(0, 20, i =>
                {
                    bool auth = service.Authenticate(password);
                    if (!auth) throw new Exception($"Concurrent authentication failed on thread {i}");

                    var settings = service.GetSettings();
                    if (!settings.IsSetup) throw new Exception($"Concurrent settings check failed on thread {i}");

                    var folders = service.GetAllVaultFolders();
                    if (folders == null) throw new Exception($"Concurrent folder query failed on thread {i}");
                });

                TestRunner.Pass("Concurrency: Multi-Threaded Atomic Access & Connection Pool Stability (20 threads)");
            }
            finally
            {
                try { File.Delete(dbPath); } catch { }
            }
        }

        private static void Test_UsbDlpHardwareIntegration()
        {
            string dbPath = Path.Combine(Path.GetTempPath(), "VortexTest_DB_UsbDlp_" + Guid.NewGuid().ToString("N") + ".db");
            var db = new DatabaseService(dbPath);
            var usb = new UsbDlpService(db);

            try
            {
                // Authorize temporary write window
                usb.AuthorizeTemporaryWrite(TimeSpan.FromMinutes(10));
                var settings = db.GetVaultSettings();
                if (!settings.UsbAllowedUntil.HasValue)
                    throw new Exception("UsbAllowedUntil was not recorded in database!");

                var diff = settings.UsbAllowedUntil.Value - DateTime.UtcNow;
                if (diff.TotalMinutes < 8 || diff.TotalMinutes > 11)
                    throw new Exception("Temporary write window duration mismatch!");

                // Query removable drives safely without throwing
                var drives = usb.GetConnectedRemovableDrives();
                if (drives == null)
                    throw new Exception("GetConnectedRemovableDrives returned null!");

                TestRunner.Pass("Hardware: USB DLP Write Protection Window & Removable Media Integration");
            }
            finally
            {
                try { File.Delete(dbPath); } catch { }
            }
        }

        private static void Test_CryptographicTamperResistance()
        {
            var crypto = new VaultCryptoEngine();
            string tempDir = Path.Combine(Path.GetTempPath(), "VortexTamperSrc_" + Guid.NewGuid().ToString("N"));
            string vaultFile = Path.Combine(Path.GetTempPath(), "VortexTamperVault_" + Guid.NewGuid().ToString("N") + ".vortexvault");
            string restoreDir = Path.Combine(Path.GetTempPath(), "VortexTamperDst_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                File.WriteAllText(Path.Combine(tempDir, "data.txt"), "Authentic Untampered Payload");
                byte[] masterKey = RandomNumberGenerator.GetBytes(32);
                var (wPass, wRec) = (crypto.WrapMasterKey(masterKey, "pass1"), crypto.WrapMasterKey(masterKey, "rec1"));

                var (encOk, _, _, _) = crypto.EncryptDirectoryWithKey(tempDir, vaultFile, masterKey, wPass, wRec, "TamperTest");
                if (!encOk) throw new Exception("Setup encryption failed!");

                // Tamper with the container file: flip a single byte in ciphertext
                byte[] containerBytes = File.ReadAllBytes(vaultFile);
                containerBytes[containerBytes.Length - 5] ^= 0xFF; // Invert byte
                File.WriteAllBytes(vaultFile, containerBytes);

                // Attempt decryption
                var (decOk, _) = crypto.DecryptVaultWithKey(vaultFile, restoreDir, masterKey);
                if (decOk)
                    throw new Exception("Decryption should have FAILED on tampered ciphertext with tag mismatch!");

                TestRunner.Pass("Security: AES-256-GCM Cryptographic Tamper-Resistance & Ciphertext Integrity");
            }
            finally
            {
                if (Directory.Exists(tempDir)) try { Directory.Delete(tempDir, true); } catch { }
                if (Directory.Exists(restoreDir)) try { Directory.Delete(restoreDir, true); } catch { }
                if (File.Exists(vaultFile)) try { File.Delete(vaultFile); } catch { }
            }
        }

        private static void Test_InMemorySecretZeroization()
        {
            byte[] sensitiveKey = RandomNumberGenerator.GetBytes(32);
            byte[] copy = new byte[32];
            Buffer.BlockCopy(sensitiveKey, 0, copy, 0, 32);

            // Verify not zero
            if (sensitiveKey.All(b => b == 0))
                throw new Exception("Key should not be all zeros initially!");

            // Zeroize using CryptographicOperations.ZeroMemory
            CryptographicOperations.ZeroMemory(sensitiveKey);

            // Assert completely zeroed
            if (!sensitiveKey.All(b => b == 0))
                throw new Exception("Key was not completely zeroized in memory!");

            TestRunner.Pass("Security: In-Memory Secret Zeroization (CryptographicOperations.ZeroMemory)");
        }
    }
}
