using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Vortex.Core.Vault
{
    /// <summary>
    /// Cryptographic engine utilizing hardware-accelerated AES-256-GCM and PBKDF2
    /// for Zero-Trust file and folder encryption.
    /// </summary>
    public class VaultCryptoEngine
    {
        private const int SaltSizeBytes = 32;
        private const int KeySizeBytes = 32;     // 256-bit key
        private const int NonceSizeBytes = 12;   // 96-bit nonce for AES-GCM
        private const int TagSizeBytes = 16;     // 128-bit authentication tag
        private const int Pbkdf2Iterations = 100_000;

        // Magic header for Vortex Encrypted Containers: "VRTX_VAULT_V1"
        private static readonly byte[] MagicHeader = Encoding.UTF8.GetBytes("VRTX_VAULT_V1");

        /// <summary>
        /// Hashes a master password with a cryptographically random salt using PBKDF2.
        /// </summary>
        public (string hash, string salt) HashPassword(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySizeBytes);

            return (Convert.ToBase64String(key), Convert.ToBase64String(salt));
        }

        /// <summary>
        /// Verifies a provided password against a stored PBKDF2 hash and salt.
        /// </summary>
        public bool VerifyPassword(string password, string storedHash, string storedSalt)
        {
            try
            {
                if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash) || string.IsNullOrEmpty(storedSalt))
                    return false;

                byte[] salt = Convert.FromBase64String(storedSalt);
                byte[] computedKey = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySizeBytes);
                byte[] expectedKey = Convert.FromBase64String(storedHash);

                return CryptographicOperations.FixedTimeEquals(computedKey, expectedKey);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Generates a human-friendly 24-character Emergency Recovery Key formatted as VRTX-XXXX-XXXX-XXXX-XXXX-XXXX.
        /// </summary>
        public (string plainKey, string keyHash) GenerateRecoveryKey()
        {
            const string chars = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ"; // Base32 without ambiguous chars (0, 1, I, O)
            var bytes = RandomNumberGenerator.GetBytes(20);
            var sb = new StringBuilder("VRTX-");

            for (int i = 0; i < 20; i++)
            {
                if (i > 0 && i % 4 == 0) sb.Append('-');
                sb.Append(chars[bytes[i] % chars.Length]);
            }

            string plainKey = sb.ToString();
            byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(plainKey.Replace("-", "").ToUpperInvariant()));
            string keyHash = Convert.ToBase64String(hashBytes);

            return (plainKey, keyHash);
        }

        /// <summary>
        /// Verifies an Emergency Recovery Key against its SHA-256 hash.
        /// </summary>
        public bool VerifyRecoveryKey(string plainKey, string storedKeyHash)
        {
            try
            {
                if (string.IsNullOrEmpty(plainKey) || string.IsNullOrEmpty(storedKeyHash)) return false;
                string clean = plainKey.Trim().Replace("-", "").Replace(" ", "").ToUpperInvariant();
                byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(clean));
                byte[] expected = Convert.FromBase64String(storedKeyHash);

                return CryptographicOperations.FixedTimeEquals(hashBytes, expected);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Derives a 256-bit encryption key from a password and salt.
        /// </summary>
        public byte[] DeriveKey(string password, byte[] salt)
        {
            return Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySizeBytes);
        }

        /// <summary>
        /// Generates a fresh random 256-bit Vault Master Key (VMK).
        /// </summary>
        public byte[] GenerateVaultMasterKey()
        {
            return RandomNumberGenerator.GetBytes(KeySizeBytes);
        }

        /// <summary>
        /// Encrypts (wraps) the 256-bit Vault Master Key with a user credential (password or recovery key).
        /// Output container format: Base64([Salt(32)] [Nonce(12)] [Tag(16)] [EncryptedVMK(32)])
        /// </summary>
        public string WrapMasterKey(byte[] masterKey, string credential)
        {
            if (masterKey == null || masterKey.Length != KeySizeBytes)
                throw new ArgumentException("Master key must be exactly 32 bytes (256-bit).", nameof(masterKey));

            string cleanCred = credential.Trim();
            if (cleanCred.StartsWith("VRTX-", StringComparison.OrdinalIgnoreCase))
            {
                cleanCred = cleanCred.Replace("-", "").ToUpperInvariant();
            }

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
            byte[] kek = DeriveKey(cleanCred, salt);
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
            byte[] tag = new byte[TagSizeBytes];
            byte[] ciphertext = new byte[masterKey.Length];

            using (var aesGcm = new AesGcm(kek, TagSizeBytes))
            {
                aesGcm.Encrypt(nonce, masterKey, ciphertext, tag, MagicHeader);
            }

            byte[] combined = new byte[SaltSizeBytes + NonceSizeBytes + TagSizeBytes + ciphertext.Length];
            Buffer.BlockCopy(salt, 0, combined, 0, SaltSizeBytes);
            Buffer.BlockCopy(nonce, 0, combined, SaltSizeBytes, NonceSizeBytes);
            Buffer.BlockCopy(tag, 0, combined, SaltSizeBytes + NonceSizeBytes, TagSizeBytes);
            Buffer.BlockCopy(ciphertext, 0, combined, SaltSizeBytes + NonceSizeBytes + TagSizeBytes, ciphertext.Length);

            return Convert.ToBase64String(combined);
        }

        /// <summary>
        /// Decrypts (unwraps) the 256-bit Vault Master Key using a user credential (password or recovery key).
        /// </summary>
        public (bool success, byte[] masterKey, string error) UnwrapMasterKey(string wrappedKeyBase64, string credential)
        {
            try
            {
                if (string.IsNullOrEmpty(wrappedKeyBase64) || string.IsNullOrEmpty(credential))
                    return (false, Array.Empty<byte>(), "Wrapped key or credential is empty.");

                byte[] combined = Convert.FromBase64String(wrappedKeyBase64);
                int minLen = SaltSizeBytes + NonceSizeBytes + TagSizeBytes + KeySizeBytes;
                if (combined.Length < minLen)
                    return (false, Array.Empty<byte>(), "Wrapped key data is malformed.");

                string cleanCred = credential.Trim();
                if (cleanCred.StartsWith("VRTX-", StringComparison.OrdinalIgnoreCase))
                {
                    cleanCred = cleanCred.Replace("-", "").ToUpperInvariant();
                }

                byte[] salt = new byte[SaltSizeBytes];
                byte[] nonce = new byte[NonceSizeBytes];
                byte[] tag = new byte[TagSizeBytes];
                byte[] ciphertext = new byte[combined.Length - (SaltSizeBytes + NonceSizeBytes + TagSizeBytes)];

                Buffer.BlockCopy(combined, 0, salt, 0, SaltSizeBytes);
                Buffer.BlockCopy(combined, SaltSizeBytes, nonce, 0, NonceSizeBytes);
                Buffer.BlockCopy(combined, SaltSizeBytes + NonceSizeBytes, tag, 0, TagSizeBytes);
                Buffer.BlockCopy(combined, SaltSizeBytes + NonceSizeBytes + TagSizeBytes, ciphertext, 0, ciphertext.Length);

                byte[] kek = DeriveKey(cleanCred, salt);
                byte[] masterKey = new byte[ciphertext.Length];

                using (var aesGcm = new AesGcm(kek, TagSizeBytes))
                {
                    aesGcm.Decrypt(nonce, ciphertext, tag, masterKey, MagicHeader);
                }

                return (true, masterKey, string.Empty);
            }
            catch (CryptographicException)
            {
                return (false, Array.Empty<byte>(), "Authentication failed. Incorrect credential.");
            }
            catch (Exception ex)
            {
                return (false, Array.Empty<byte>(), ex.Message);
            }
        }

        /// <summary>
        /// Encrypts an entire directory into an authenticated AES-256-GCM container file using a Vault Master Key.
        /// </summary>
        public (bool success, string error, long totalBytes, int fileCount) EncryptDirectoryWithKey(
            string sourceDirectory, string destinationVaultFile, byte[] masterKey)
        {
            try
            {
                if (!Directory.Exists(sourceDirectory))
                    return (false, "Source directory does not exist.", 0, 0);

                if (masterKey == null || masterKey.Length != KeySizeBytes)
                    return (false, "Invalid Vault Master Key.", 0, 0);

                var dirInfo = new DirectoryInfo(sourceDirectory);
                var allFiles = dirInfo.GetFiles("*", SearchOption.AllDirectories);
                long totalBytes = 0;
                foreach (var f in allFiles) totalBytes += f.Length;
                int fileCount = allFiles.Length;

                // 1. Pack directory contents into a temporary zip archive in memory / temp file
                string tempZip = Path.Combine(Path.GetTempPath(), $"vrtx_tmp_{Guid.NewGuid():N}.zip");
                try
                {
                    ZipFile.CreateFromDirectory(sourceDirectory, tempZip, CompressionLevel.Fastest, false);
                    byte[] zipBytes = File.ReadAllBytes(tempZip);

                    byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
                    byte[] nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
                    byte[] tag = new byte[TagSizeBytes];
                    byte[] ciphertext = new byte[zipBytes.Length];

                    // 2. Encrypt payload with AES-256-GCM using direct VMK
                    using (var aesGcm = new AesGcm(masterKey, TagSizeBytes))
                    {
                        aesGcm.Encrypt(nonce, zipBytes, ciphertext, tag, MagicHeader);
                    }

                    // 3. Write atomic container: [MagicHeader(13)] [Salt(32)] [Nonce(12)] [Tag(16)] [Ciphertext(N)]
                    string tempOut = destinationVaultFile + ".tmp";
                    using (var fs = new FileStream(tempOut, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var bw = new BinaryWriter(fs))
                    {
                        bw.Write(MagicHeader);
                        bw.Write(salt);
                        bw.Write(nonce);
                        bw.Write(tag);
                        bw.Write(ciphertext);
                    }

                    if (File.Exists(destinationVaultFile)) File.Delete(destinationVaultFile);
                    File.Move(tempOut, destinationVaultFile);

                    return (true, string.Empty, totalBytes, fileCount);
                }
                finally
                {
                    if (File.Exists(tempZip)) File.Delete(tempZip);
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message, 0, 0);
            }
        }

        /// <summary>
        /// Decrypts a Vortex Encrypted Container file using a Vault Master Key.
        /// </summary>
        public (bool success, string error) DecryptVaultWithKey(
            string vaultFile, string targetDirectory, byte[] masterKey)
        {
            try
            {
                if (!File.Exists(vaultFile))
                    return (false, "Vault container file was not found.");

                if (masterKey == null || masterKey.Length != KeySizeBytes)
                    return (false, "Invalid Vault Master Key.");

                byte[] fileBytes = File.ReadAllBytes(vaultFile);
                if (fileBytes.Length < MagicHeader.Length + SaltSizeBytes + NonceSizeBytes + TagSizeBytes)
                    return (false, "Corrupt or invalid vault container format.");

                using var ms = new MemoryStream(fileBytes);
                using var br = new BinaryReader(ms);

                byte[] header = br.ReadBytes(MagicHeader.Length);
                if (!CryptographicOperations.FixedTimeEquals(header, MagicHeader))
                    return (false, "Invalid vault header. This file is not a valid Vortex container.");

                byte[] salt = br.ReadBytes(SaltSizeBytes);
                byte[] nonce = br.ReadBytes(NonceSizeBytes);
                byte[] tag = br.ReadBytes(TagSizeBytes);
                byte[] ciphertext = br.ReadBytes((int)(ms.Length - ms.Position));

                byte[] plaintextZip = new byte[ciphertext.Length];

                try
                {
                    using (var aesGcm = new AesGcm(masterKey, TagSizeBytes))
                    {
                        aesGcm.Decrypt(nonce, ciphertext, tag, plaintextZip, MagicHeader);
                    }
                }
                catch (CryptographicException)
                {
                    return (false, "Incorrect Master Password or authentication failed. Access Denied.");
                }

                // Unpack zip safely into target directory
                string tempZip = Path.Combine(Path.GetTempPath(), $"vrtx_restore_{Guid.NewGuid():N}.zip");
                try
                {
                    File.WriteAllBytes(tempZip, plaintextZip);

                    if (!Directory.Exists(targetDirectory))
                        Directory.CreateDirectory(targetDirectory);

                    ZipFile.ExtractToDirectory(tempZip, targetDirectory, overwriteFiles: true);
                    return (true, string.Empty);
                }
                finally
                {
                    if (File.Exists(tempZip)) File.Delete(tempZip);
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Legacy fallback: Encrypts a directory with direct password derivation.
        /// </summary>
        public (bool success, string error, long totalBytes, int fileCount) EncryptDirectoryToVault(
            string sourceDirectory, string destinationVaultFile, string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
            byte[] key = DeriveKey(password, salt);
            return EncryptDirectoryWithKey(sourceDirectory, destinationVaultFile, key);
        }

        /// <summary>
        /// Legacy fallback: Decrypts a directory trying direct password derivation.
        /// </summary>
        public (bool success, string error) DecryptVaultToDirectory(
            string vaultFile, string targetDirectory, string password)
        {
            try
            {
                if (!File.Exists(vaultFile))
                    return (false, "Vault container file was not found.");

                byte[] fileBytes = File.ReadAllBytes(vaultFile);
                if (fileBytes.Length < MagicHeader.Length + SaltSizeBytes + NonceSizeBytes + TagSizeBytes)
                    return (false, "Corrupt or invalid vault container format.");

                using var ms = new MemoryStream(fileBytes);
                using var br = new BinaryReader(ms);

                byte[] header = br.ReadBytes(MagicHeader.Length);
                if (!CryptographicOperations.FixedTimeEquals(header, MagicHeader))
                    return (false, "Invalid vault header. This file is not a valid Vortex container.");

                byte[] salt = br.ReadBytes(SaltSizeBytes);
                byte[] nonce = br.ReadBytes(NonceSizeBytes);
                byte[] tag = br.ReadBytes(TagSizeBytes);
                byte[] ciphertext = br.ReadBytes((int)(ms.Length - ms.Position));

                // Try direct derivation from salt
                byte[] key = DeriveKey(password, salt);
                byte[] plaintextZip = new byte[ciphertext.Length];

                try
                {
                    using (var aesGcm = new AesGcm(key, TagSizeBytes))
                    {
                        aesGcm.Decrypt(nonce, ciphertext, tag, plaintextZip, MagicHeader);
                    }
                }
                catch (CryptographicException)
                {
                    return (false, "Incorrect Master Password or authentication failed. Access Denied.");
                }

                string tempZip = Path.Combine(Path.GetTempPath(), $"vrtx_restore_{Guid.NewGuid():N}.zip");
                try
                {
                    File.WriteAllBytes(tempZip, plaintextZip);

                    if (!Directory.Exists(targetDirectory))
                        Directory.CreateDirectory(targetDirectory);

                    ZipFile.ExtractToDirectory(tempZip, targetDirectory, overwriteFiles: true);
                    return (true, string.Empty);
                }
                finally
                {
                    if (File.Exists(tempZip)) File.Delete(tempZip);
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
