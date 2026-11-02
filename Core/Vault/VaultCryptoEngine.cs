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

        // Magic header for Vortex Encrypted Containers: "VRTX_VAULT_V1" and V2 self-contained
        private static readonly byte[] MagicHeader = Encoding.UTF8.GetBytes("VRTX_VAULT_V1");
        private static readonly byte[] MagicHeaderV2 = Encoding.UTF8.GetBytes("VRTX_VAULT_V2");

        /// <summary>
        /// Canonicalizes an Emergency Recovery Key into its pure alphanumeric base32 representation.
        /// Handles optional "VRTX-" prefix, lowercase, hyphens, and spaces cleanly.
        /// </summary>
        public static string NormalizeRecoveryKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;
            string clean = key.Trim().ToUpperInvariant();
            if (clean.StartsWith("VRTX-", StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(5);
            else if (clean.StartsWith("VRTX", StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(4);
            return clean.Replace("-", "").Replace(" ", "");
        }

        /// <summary>
        /// Formats a canonical recovery key into user-friendly standard format: VRTX-XXXX-XXXX-XXXX-XXXX-XXXX.
        /// </summary>
        public static string FormatRecoveryKey(string cleanKey)
        {
            string clean = NormalizeRecoveryKey(cleanKey);
            var sb = new StringBuilder("VRTX-");
            for (int i = 0; i < clean.Length; i++)
            {
                if (i > 0 && i % 4 == 0) sb.Append('-');
                sb.Append(clean[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Computes the SHA-256 hash of a normalized Emergency Recovery Key.
        /// </summary>
        public string ComputeRecoveryKeyHash(string plainKey)
        {
            string clean = NormalizeRecoveryKey(plainKey);
            byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(clean));
            return Convert.ToBase64String(hashBytes);
        }

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
            var sb = new StringBuilder();

            for (int i = 0; i < 20; i++)
            {
                sb.Append(chars[bytes[i] % chars.Length]);
            }

            string clean = sb.ToString();
            string plainKey = FormatRecoveryKey(clean);
            string keyHash = ComputeRecoveryKeyHash(clean);

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
                string clean = NormalizeRecoveryKey(plainKey);
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
        /// Checks if a credential string appears to be an Emergency Recovery Key (formatted or normalized).
        /// </summary>
        private static bool IsRecoveryKeyFormat(string credential)
        {
            if (string.IsNullOrWhiteSpace(credential)) return false;
            string clean = credential.Trim();
            if (clean.StartsWith("VRTX-", StringComparison.OrdinalIgnoreCase) || clean.StartsWith("VRTX", StringComparison.OrdinalIgnoreCase))
                return true;

            string normalized = NormalizeRecoveryKey(clean);
            return normalized.Length == 20 && normalized.All(c => "23456789ABCDEFGHJKLMNPQRSTUVWXYZ".Contains(c));
        }

        /// <summary>
        /// Encrypts (wraps) the 256-bit Vault Master Key with a user credential (password or recovery key).
        /// Output container format: Base64([Salt(32)] [Nonce(12)] [Tag(16)] [EncryptedVMK(32)])
        /// </summary>
        public string WrapMasterKey(byte[] masterKey, string credential)
        {
            if (masterKey == null || masterKey.Length != KeySizeBytes)
                throw new ArgumentException("Master key must be exactly 32 bytes (256-bit).", nameof(masterKey));

            string cleanCred = IsRecoveryKeyFormat(credential) ? NormalizeRecoveryKey(credential) : credential.Trim();

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

                string cleanCred = IsRecoveryKeyFormat(credential) ? NormalizeRecoveryKey(credential) : credential.Trim();

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
        /// Supports self-contained V2 format (embedding wrapped keys for zero-loss recovery) with backward-compatible V1 fallback.
        /// </summary>
        public (bool success, string error, long totalBytes, int fileCount) EncryptDirectoryWithKey(
            string sourceDirectory,
            string destinationVaultFile,
            byte[] masterKey,
            string? wrappedPasswordKey = null,
            string? wrappedRecoveryKey = null,
            string? folderName = null)
        {
            try
            {
                if (!Directory.Exists(sourceDirectory))
                    return (false, "Source directory does not exist.", 0, 0);

                if (masterKey == null || masterKey.Length != KeySizeBytes)
                    return (false, "Invalid Vault Master Key.", 0, 0);

                // 1. Pack directory contents into a temporary zip archive in temp file
                // Skip existing vault containers, backup files, and lock barriers
                string tempZip = Path.Combine(Path.GetTempPath(), $"vrtx_tmp_{Guid.NewGuid():N}.zip");
                long totalBytes = 0;
                int fileCount = 0;

                try
                {
                    using (var zipStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
                    {
                        var dirInfo = new DirectoryInfo(sourceDirectory);
                        int rootLen = dirInfo.FullName.TrimEnd('\\', '/').Length + 1;

                        // Preserve empty subdirectories
                        foreach (var subDir in dirInfo.GetDirectories("*", SearchOption.AllDirectories))
                        {
                            string relDir = subDir.FullName.Substring(rootLen).Replace('\\', '/') + "/";
                            archive.CreateEntry(relDir);
                        }

                        // Archive regular user files
                        foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                        {
                            if (file.Name.Equals("🔒_LOCKED_BY_VORTEX_VAULT.txt", StringComparison.OrdinalIgnoreCase) ||
                                file.Name.EndsWith(".vortexvault", StringComparison.OrdinalIgnoreCase) ||
                                file.Name.EndsWith(".vortexvault.bak", StringComparison.OrdinalIgnoreCase) ||
                                file.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            string relPath = file.FullName.Substring(rootLen);
                            archive.CreateEntryFromFile(file.FullName, relPath, CompressionLevel.Fastest);
                            totalBytes += file.Length;
                            fileCount++;
                        }
                    }

                    byte[] zipBytes = File.ReadAllBytes(tempZip);

                    byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
                    byte[] nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
                    byte[] tag = new byte[TagSizeBytes];
                    byte[] ciphertext = new byte[zipBytes.Length];

                    bool isV2 = !string.IsNullOrEmpty(wrappedPasswordKey) && !string.IsNullOrEmpty(wrappedRecoveryKey);
                    byte[] activeHeader = isV2 ? MagicHeaderV2 : MagicHeader;

                    // 2. Encrypt payload with AES-256-GCM using direct VMK
                    using (var aesGcm = new AesGcm(masterKey, TagSizeBytes))
                    {
                        aesGcm.Encrypt(nonce, zipBytes, ciphertext, tag, activeHeader);
                    }

                    // 3. Write atomic container via temp file in %TEMP%
                    string tempOut = Path.Combine(Path.GetTempPath(), $"vrtx_enc_{Guid.NewGuid():N}.tmp");
                    using (var fs = new FileStream(tempOut, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var bw = new BinaryWriter(fs, Encoding.UTF8))
                    {
                        if (isV2)
                        {
                            // V2 Self-Contained Format
                            bw.Write(MagicHeaderV2);
                            bw.Write((int)2); // Version 2
                            bw.Write(folderName ?? Path.GetFileName(sourceDirectory));
                            bw.Write(wrappedPasswordKey!);
                            bw.Write(wrappedRecoveryKey!);
                            bw.Write(nonce);
                            bw.Write(tag);
                            bw.Write(ciphertext);
                        }
                        else
                        {
                            // Legacy V1 Format
                            bw.Write(MagicHeader);
                            bw.Write(salt);
                            bw.Write(nonce);
                            bw.Write(tag);
                            bw.Write(ciphertext);
                        }
                    }

                    // Ensure target parent directory exists
                    string? targetDir = Path.GetDirectoryName(destinationVaultFile);
                    if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    if (File.Exists(destinationVaultFile))
                    {
                        try
                        {
                            // Remove hidden/system flags if any so overwrite succeeds
                            File.SetAttributes(destinationVaultFile, FileAttributes.Normal);
                        }
                        catch { }
                        File.Delete(destinationVaultFile);
                    }
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
        /// Decrypts a Vortex Encrypted Container file using a Vault Master Key (supports both V1 and V2 formats).
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

                using var fs = new FileStream(vaultFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var br = new BinaryReader(fs, Encoding.UTF8);

                byte[] header = br.ReadBytes(MagicHeader.Length);
                bool isV2 = CryptographicOperations.FixedTimeEquals(header, MagicHeaderV2);
                bool isV1 = CryptographicOperations.FixedTimeEquals(header, MagicHeader);

                if (!isV1 && !isV2)
                    return (false, "Invalid vault header. This file is not a valid Vortex container.");

                byte[] nonce;
                byte[] tag;
                byte[] ciphertext;
                byte[] activeHeader = isV2 ? MagicHeaderV2 : MagicHeader;

                if (isV2)
                {
                    int version = br.ReadInt32();
                    string folderName = br.ReadString();
                    string wrappedPass = br.ReadString();
                    string wrappedRec = br.ReadString();
                    nonce = br.ReadBytes(NonceSizeBytes);
                    tag = br.ReadBytes(TagSizeBytes);
                    ciphertext = br.ReadBytes((int)(fs.Length - fs.Position));
                }
                else
                {
                    byte[] salt = br.ReadBytes(SaltSizeBytes);
                    nonce = br.ReadBytes(NonceSizeBytes);
                    tag = br.ReadBytes(TagSizeBytes);
                    ciphertext = br.ReadBytes((int)(fs.Length - fs.Position));
                }

                byte[] plaintextZip = new byte[ciphertext.Length];

                try
                {
                    using (var aesGcm = new AesGcm(masterKey, TagSizeBytes))
                    {
                        aesGcm.Decrypt(nonce, ciphertext, tag, plaintextZip, activeHeader);
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
        /// Standalone rescue: Decrypts any container directly using user credentials (password or recovery key).
        /// For V2 containers, unwraps the VMK directly from the container file itself, requiring ZERO database state.
        /// </summary>
        public (bool success, string error) DecryptVaultToDirectory(
            string vaultFile, string targetDirectory, string credential)
        {
            try
            {
                if (!File.Exists(vaultFile))
                    return (false, "Vault container file was not found.");

                if (string.IsNullOrWhiteSpace(credential))
                    return (false, "Credential cannot be empty.");

                using var fs = new FileStream(vaultFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var br = new BinaryReader(fs, Encoding.UTF8);

                byte[] header = br.ReadBytes(MagicHeader.Length);
                bool isV2 = CryptographicOperations.FixedTimeEquals(header, MagicHeaderV2);
                bool isV1 = CryptographicOperations.FixedTimeEquals(header, MagicHeader);

                if (!isV1 && !isV2)
                    return (false, "Invalid vault header. This file is not a valid Vortex container.");

                if (isV2)
                {
                    int version = br.ReadInt32();
                    string folderName = br.ReadString();
                    string wrappedPass = br.ReadString();
                    string wrappedRec = br.ReadString();
                    byte[] nonce = br.ReadBytes(NonceSizeBytes);
                    byte[] tag = br.ReadBytes(TagSizeBytes);
                    byte[] ciphertext = br.ReadBytes((int)(fs.Length - fs.Position));

                    // Try unwrapping VMK with credential via password slot first, then recovery slot
                    byte[]? unwrappedVmk = null;
                    var (pUnwrapped, pVmk, _) = UnwrapMasterKey(wrappedPass, credential);
                    if (pUnwrapped)
                    {
                        unwrappedVmk = pVmk;
                    }
                    else
                    {
                        var (rUnwrapped, rVmk, _) = UnwrapMasterKey(wrappedRec, credential);
                        if (rUnwrapped)
                        {
                            unwrappedVmk = rVmk;
                        }
                    }

                    if (unwrappedVmk == null)
                        return (false, "Authentication failed. Incorrect Master Password or Recovery Key.");

                    byte[] plaintextZip = new byte[ciphertext.Length];
                    try
                    {
                        using (var aesGcm = new AesGcm(unwrappedVmk, TagSizeBytes))
                        {
                            aesGcm.Decrypt(nonce, ciphertext, tag, plaintextZip, MagicHeaderV2);
                        }
                    }
                    catch (CryptographicException)
                    {
                        return (false, "Decryption authentication failed. Container integrity compromised.");
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
                else
                {
                    // Legacy V1 format fallback: direct derivation from salt
                    byte[] salt = br.ReadBytes(SaltSizeBytes);
                    byte[] nonce = br.ReadBytes(NonceSizeBytes);
                    byte[] tag = br.ReadBytes(TagSizeBytes);
                    byte[] ciphertext = br.ReadBytes((int)(fs.Length - fs.Position));

                    byte[] key = DeriveKey(credential, salt);
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
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Probes and extracts the 256-bit Vault Master Key (VMK) directly from a container file
        /// using a candidate credential (password or recovery key).
        /// Supports self-contained V2 containers.
        /// </summary>
        public (bool success, byte[] vmk, string error) ExtractVmkFromContainer(string vaultFile, string credential)
        {
            try
            {
                if (!File.Exists(vaultFile))
                    return (false, Array.Empty<byte>(), "Vault container file was not found.");

                if (string.IsNullOrWhiteSpace(credential))
                    return (false, Array.Empty<byte>(), "Credential cannot be empty.");

                using var fs = new FileStream(vaultFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var br = new BinaryReader(fs, Encoding.UTF8);

                byte[] header = br.ReadBytes(MagicHeader.Length);
                bool isV2 = CryptographicOperations.FixedTimeEquals(header, MagicHeaderV2);

                if (!isV2)
                {
                    // If V1 legacy, try deriving key directly from salt
                    bool isV1 = CryptographicOperations.FixedTimeEquals(header, MagicHeader);
                    if (!isV1)
                        return (false, Array.Empty<byte>(), "File is not a valid Vortex container.");

                    byte[] salt = br.ReadBytes(SaltSizeBytes);
                    byte[] nonce = br.ReadBytes(NonceSizeBytes);
                    byte[] tag = br.ReadBytes(TagSizeBytes);
                    byte[] ciphertext = br.ReadBytes((int)(fs.Length - fs.Position));
                    byte[] candidateKey = DeriveKey(credential, salt);
                    byte[] testPlain = new byte[ciphertext.Length];
                    try
                    {
                        using var aesGcm = new AesGcm(candidateKey, TagSizeBytes);
                        aesGcm.Decrypt(nonce, ciphertext, tag, testPlain, MagicHeader);
                        return (true, candidateKey, string.Empty);
                    }
                    catch
                    {
                        return (false, Array.Empty<byte>(), "Incorrect credential for container.");
                    }
                }

                int version = br.ReadInt32();
                string folderName = br.ReadString();
                string wrappedPass = br.ReadString();
                string wrappedRec = br.ReadString();

                // 1. Try unwrapping with password slot
                var (pOk, pVmk, _) = UnwrapMasterKey(wrappedPass, credential);
                if (pOk) return (true, pVmk, string.Empty);

                // 2. Try unwrapping with recovery key slot
                var (rOk, rVmk, _) = UnwrapMasterKey(wrappedRec, credential);
                if (rOk) return (true, rVmk, string.Empty);

                return (false, Array.Empty<byte>(), "Incorrect credential. Could not unwrap key from container.");
            }
            catch (Exception ex)
            {
                return (false, Array.Empty<byte>(), ex.Message);
            }
        }
    }
}
