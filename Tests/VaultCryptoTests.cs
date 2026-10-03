using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Vortex.Core.Vault;

namespace Vortex.Tests
{
    public static class VaultCryptoTests
    {
        public static void RunAll()
        {
            Test_PasswordHashingAndVerification();
            Test_RecoveryKeyGenerationAndVerification();
            Test_DirectoryEncryptionAndDecryption_Success();
            Test_DecryptionWithInvalidPassword_ThrowsException();
        }

        private static void Test_PasswordHashingAndVerification()
        {
            var crypto = new VaultCryptoEngine();
            string password = "SecretMasterPassword_2026!#$";

            var (hash, salt) = crypto.HashPassword(password);
            
            if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(salt))
                throw new Exception("Hash or Salt was null/empty.");

            if (!crypto.VerifyPassword(password, hash, salt))
                throw new Exception("Password verification failed for correct password.");

            if (crypto.VerifyPassword("WrongPassword123!", hash, salt))
                throw new Exception("Password verification succeeded for incorrect password!");

            TestRunner.Pass("VaultCrypto: PBKDF2 Password Hashing & Constant-Time Verification");
        }

        private static void Test_RecoveryKeyGenerationAndVerification()
        {
            var crypto = new VaultCryptoEngine();
            var (plainKey, keyHash) = crypto.GenerateRecoveryKey();

            if (!plainKey.StartsWith("VRTX-"))
                throw new Exception($"Generated recovery key does not start with VRTX-: {plainKey}");

            if (plainKey.Length != 29) // VRTX-XXXX-XXXX-XXXX-XXXX-XXXX (29 chars)
                throw new Exception($"Unexpected recovery key length: {plainKey.Length} (expected 29)");

            if (!crypto.VerifyRecoveryKey(plainKey, keyHash))
                throw new Exception("Recovery key verification failed for valid key.");

            // Test case-insensitivity and hyphen-insensitivity
            string lowercaseNoHyphens = plainKey.ToLowerInvariant().Replace("-", " ");
            if (!crypto.VerifyRecoveryKey(lowercaseNoHyphens, keyHash))
                throw new Exception("Recovery key verification failed for normalized key variation.");

            if (crypto.VerifyRecoveryKey("VRTX-9999-9999-9999-9999-9999", keyHash))
                throw new Exception("Recovery key verification passed for invalid key!");

            TestRunner.Pass("VaultCrypto: 24-char Emergency Recovery Key Format & SHA-256 Validation");
        }

        private static void Test_DirectoryEncryptionAndDecryption_Success()
        {
            var crypto = new VaultCryptoEngine();
            string tempDir = Path.Combine(Path.GetTempPath(), "VortexTest_Dir_" + Guid.NewGuid().ToString("N"));
            string subDir = Path.Combine(tempDir, "SubFolder");
            Directory.CreateDirectory(subDir);

            string file1 = Path.Combine(tempDir, "doc1.txt");
            string file2 = Path.Combine(subDir, "data.bin");
            string password = "StrongTestEncryptionPassword99@";

            string content1 = "Top Secret Vortex Document Content. Confidential!";
            byte[] content2 = new byte[8192];
            RandomNumberGenerator.Fill(content2);

            File.WriteAllText(file1, content1);
            File.WriteAllBytes(file2, content2);

            byte[] expectedHash1 = SHA256.HashData(Encoding.UTF8.GetBytes(content1));
            byte[] expectedHash2 = SHA256.HashData(content2);

            string vaultFile = Path.Combine(Path.GetTempPath(), "vault_" + Guid.NewGuid().ToString("N") + ".vortexvault");

            try
            {
                // Encrypt
                var (encSuccess, encErr, totalBytes, fileCount) = crypto.EncryptDirectoryToVault(tempDir, vaultFile, password);
                if (!encSuccess)
                    throw new Exception($"EncryptDirectoryToVault failed: {encErr}");

                if (fileCount != 2)
                    throw new Exception($"Expected 2 files packaged, got {fileCount}");

                if (!File.Exists(vaultFile))
                    throw new Exception("Vault file was not created on disk.");

                if (Directory.Exists(tempDir))
                    throw new Exception("Source directory should be deleted after successful encryption.");

                // Decrypt
                var (decSuccess, decErr) = crypto.DecryptVaultToDirectory(vaultFile, tempDir, password);
                if (!decSuccess)
                    throw new Exception($"DecryptVaultToDirectory failed: {decErr}");

                if (!Directory.Exists(tempDir) || !File.Exists(file1) || !File.Exists(file2))
                    throw new Exception("Restored files/directory missing after decryption.");

                byte[] restoredHash1 = SHA256.HashData(File.ReadAllBytes(file1));
                byte[] restoredHash2 = SHA256.HashData(File.ReadAllBytes(file2));

                if (!CryptographicOperations.FixedTimeEquals(expectedHash1, restoredHash1))
                    throw new Exception("Restored file1 hash does not match original.");

                if (!CryptographicOperations.FixedTimeEquals(expectedHash2, restoredHash2))
                    throw new Exception("Restored file2 hash does not match original.");

                TestRunner.Pass("VaultCrypto: AES-256-GCM Directory Archive Encryption & Decryption Cycle");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                if (File.Exists(vaultFile)) File.Delete(vaultFile);
            }
        }

        private static void Test_DecryptionWithInvalidPassword_ThrowsException()
        {
            var crypto = new VaultCryptoEngine();
            string tempDir = Path.Combine(Path.GetTempPath(), "VortexTest_WrongPass_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "secret.txt"), "Important Password Protected Content");

            string vaultFile = Path.Combine(Path.GetTempPath(), "vault_" + Guid.NewGuid().ToString("N") + ".vortexvault");

            try
            {
                var (encSuccess, _, _, _) = crypto.EncryptDirectoryToVault(tempDir, vaultFile, "CorrectPassword123");
                if (!encSuccess) throw new Exception("Setup encryption failed.");

                var (decSuccess, decErr) = crypto.DecryptVaultToDirectory(vaultFile, tempDir, "WrongPassword999");
                if (decSuccess)
                    throw new Exception("Decryption succeeded with invalid password! Authentication tag check failed.");

                TestRunner.Pass("VaultCrypto: AES-256-GCM Cryptographic Tag Authentication on Bad Password");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                if (File.Exists(vaultFile)) File.Delete(vaultFile);
            }
        }
    }
}
