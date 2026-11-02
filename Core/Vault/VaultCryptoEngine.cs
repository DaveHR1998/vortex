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

        // Magic headers for Vortex Encrypted Containers: V1 (legacy), V2 (self-contained), and V3 (streaming chunked)
        private static readonly byte[] MagicHeader = Encoding.UTF8.GetBytes("VRTX_VAULT_V1");
        private static readonly byte[] MagicHeaderV2 = Encoding.UTF8.GetBytes("VRTX_VAULT_V2");
        private static readonly byte[] MagicHeaderV3 = Encoding.UTF8.GetBytes("VRTX_VAULT_V3");
        public const int StreamChunkSizeBytes = 1024 * 1024; // 1 MB streaming chunks for low RAM footprint

        /// <summary>
        /// Inspects a directory to verify whether any file is open, in-use, or locked by another process.
        /// Prevents archiving incomplete directories or corrupting locked files.
        /// </summary>
        public static (bool hasLocks, string? lockedFilePath) CheckDirectoryFileLocks(string directory)
        {
            try
            {
                if (!Directory.Exists(directory)) return (false, null);

                var dirInfo = new DirectoryInfo(directory);
                foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                {
                    if (file.Name.Equals("🔒_LOCKED_BY_VORTEX_VAULT.txt", StringComparison.OrdinalIgnoreCase) ||
                        file.Name.EndsWith(".vortexvault", StringComparison.OrdinalIgnoreCase) ||
                        file.Name.EndsWith(".vortexvault.bak", StringComparison.OrdinalIgnoreCase) ||
                        file.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        using var stream = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    }
                    catch (IOException)
                    {
                        return (true, file.FullName);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        return (true, file.FullName);
                    }
                    catch
                    {
                        return (true, file.FullName);
                    }
                }

                return (false, null);
            }
            catch (Exception ex)
            {
                return (true, ex.Message);
            }
        }

        /// <summary>
        /// Derives a deterministic 12-byte nonce for chunk index i using a random base nonce.
        /// Guaranteed unique per chunk to prevent AES-GCM nonce reuse.
        /// </summary>
        private static byte[] DeriveChunkNonce(byte[] baseNonce, ulong chunkIndex)
        {
            byte[] nonce = new byte[NonceSizeBytes];
            Buffer.BlockCopy(baseNonce, 0, nonce, 0, NonceSizeBytes);
            for (int b = 0; b < 8; b++)
            {
                nonce[4 + b] ^= (byte)((chunkIndex >> (b * 8)) & 0xFF);
            }
            return nonce;
        }

        /// <summary>
        /// Creates authenticated additional data binding header, chunk index, and finality flag.
        /// Prevents chunk reordering, alteration, truncation, or insertion attacks.
        /// </summary>
        private static byte[] CreateChunkAad(byte[] header, ulong chunkIndex, bool isFinal)
        {
            byte[] aad = new byte[header.Length + 9];
            Buffer.BlockCopy(header, 0, aad, 0, header.Length);
            for (int b = 0; b < 8; b++)
            {
                aad[header.Length + b] = (byte)((chunkIndex >> (b * 8)) & 0xFF);
            }
            aad[header.Length + 8] = (byte)(isFinal ? 1 : 0);
            return aad;
        }

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
        /// Supports self-contained V3 streaming format (1 MB chunked streaming AEAD with zero memory exhaustion)
        /// with backward-compatible V2 and V1 reading.
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

                // Pre-flight check: verify no files are in-use or locked by other programs
                var (hasLocks, lockedFile) = CheckDirectoryFileLocks(sourceDirectory);
                if (hasLocks)
                {
                    return (false, $"Cannot lock folder: The file '{Path.GetFileName(lockedFile)}' is open or locked by another process. Please close it and retry.", 0, 0);
                }

                // 1. Pack directory contents into a temporary zip archive in temp file
                string tempZip = Path.Combine(Path.GetTempPath(), $"vrtx_tmp_{Guid.NewGuid():N}.zip");
                long totalBytes = 0;
                int fileCount = 0;

                try
                {
                    using (var zipStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
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

                    // 2. Stream chunked encryption directly into temporary container file
                    string tempOut = Path.Combine(Path.GetTempPath(), $"vrtx_enc_{Guid.NewGuid():N}.tmp");
                    using (var outFs = new FileStream(tempOut, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
                    using (var bw = new BinaryWriter(outFs, Encoding.UTF8))
                    using (var zipFs = new FileStream(tempZip, FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
                    {
                        byte[] baseNonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);

                        // V3 Self-Contained Streaming Format
                        bw.Write(MagicHeaderV3);
                        bw.Write((int)3); // Version 3
                        bw.Write(folderName ?? Path.GetFileName(sourceDirectory));
                        bw.Write(wrappedPasswordKey ?? string.Empty);
                        bw.Write(wrappedRecoveryKey ?? string.Empty);
                        bw.Write((int)StreamChunkSizeBytes);
                        bw.Write(baseNonce);
                        bw.Write((long)zipFs.Length);

                        byte[] readBuffer = new byte[StreamChunkSizeBytes];
                        byte[] cipherBuffer = new byte[StreamChunkSizeBytes];
                        byte[] tag = new byte[TagSizeBytes];
                        ulong chunkIndex = 0;
                        long remainingBytes = zipFs.Length;

                        using (var aesGcm = new AesGcm(masterKey, TagSizeBytes))
                        {
                            do
                            {
                                int toRead = (int)Math.Min((long)readBuffer.Length, remainingBytes);
                                int bytesRead = 0;
                                while (bytesRead < toRead)
                                {
                                    int n = zipFs.Read(readBuffer, bytesRead, toRead - bytesRead);
                                    if (n <= 0) break;
                                    bytesRead += n;
                                }

                                remainingBytes -= bytesRead;
                                bool isFinal = (remainingBytes <= 0);

                                byte[] chunkNonce = DeriveChunkNonce(baseNonce, chunkIndex);
                                byte[] aad = CreateChunkAad(MagicHeaderV3, chunkIndex, isFinal);

                                aesGcm.Encrypt(
                                    chunkNonce,
                                    readBuffer.AsSpan(0, bytesRead),
                                    cipherBuffer.AsSpan(0, bytesRead),
                                    tag,
                                    aad);

                                bw.Write((int)bytesRead);
                                bw.Write(tag);
                                bw.Write(cipherBuffer, 0, bytesRead);
                                bw.Write(isFinal);

                                chunkIndex++;
                            } while (remainingBytes > 0);
                        }
                    }

                    // 3. Ensure target parent directory exists
                    string? targetDir = Path.GetDirectoryName(destinationVaultFile);
                    if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    if (File.Exists(destinationVaultFile))
                    {
                        try
                        {
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
        /// Decrypts a Vortex Encrypted Container file using a Vault Master Key (supports V1, V2, and V3 streaming formats).
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

                using var fs = new FileStream(vaultFile, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
                using var br = new BinaryReader(fs, Encoding.UTF8);

                byte[] header = br.ReadBytes(MagicHeader.Length);
                bool isV3 = CryptographicOperations.FixedTimeEquals(header, MagicHeaderV3);
                bool isV2 = CryptographicOperations.FixedTimeEquals(header, MagicHeaderV2);
                bool isV1 = CryptographicOperations.FixedTimeEquals(header, MagicHeader);

                if (!isV1 && !isV2 && !isV3)
                    return (false, "Invalid vault header. This file is not a valid Vortex container.");

                string tempZip = Path.Combine(Path.GetTempPath(), $"vrtx_restore_{Guid.NewGuid():N}.zip");
                try
                {
                    if (isV3)
                    {
                        int version = br.ReadInt32();
                        string folderName = br.ReadString();
                        string wrappedPass = br.ReadString();
                        string wrappedRec = br.ReadString();
                        int chunkSize = br.ReadInt32();
                        byte[] baseNonce = br.ReadBytes(NonceSizeBytes);
                        long uncompressedZipLength = br.ReadInt64();

                        using (var zipFs = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
                        using (var aesGcm = new AesGcm(masterKey, TagSizeBytes))
                        {
                            byte[] cipherBuffer = new byte[chunkSize];
                            byte[] plainBuffer = new byte[chunkSize];
                            ulong chunkIndex = 0;
                            bool isFinal = false;

                            while (!isFinal && fs.Position < fs.Length)
                            {
                                int chunkLen = br.ReadInt32();
                                byte[] tag = br.ReadBytes(TagSizeBytes);
                                int bytesRead = 0;
                                while (bytesRead < chunkLen)
                                {
                                    int n = fs.Read(cipherBuffer, bytesRead, chunkLen - bytesRead);
                                    if (n <= 0) break;
                                    bytesRead += n;
                                }
                                if (bytesRead != chunkLen)
                                    return (false, "Container payload is corrupted or truncated.");

                                isFinal = br.ReadBoolean();

                                byte[] chunkNonce = DeriveChunkNonce(baseNonce, chunkIndex);
                                byte[] aad = CreateChunkAad(MagicHeaderV3, chunkIndex, isFinal);

                                try
                                {
                                    aesGcm.Decrypt(
                                        chunkNonce,
                                        cipherBuffer.AsSpan(0, chunkLen),
                                        tag,
                                        plainBuffer.AsSpan(0, chunkLen),
                                        aad);
                                }
                                catch (CryptographicException)
                                {
                                    return (false, "Incorrect Master Password or authentication failed. Access Denied.");
                                }

                                zipFs.Write(plainBuffer, 0, chunkLen);
                                chunkIndex++;
                            }
                        }
                    }
                    else if (isV2)
                    {
                        int version = br.ReadInt32();
                        string folderName = br.ReadString();
                        string wrappedPass = br.ReadString();
                        string wrappedRec = br.ReadString();
                        byte[] nonce = br.ReadBytes(NonceSizeBytes);
                        byte[] tag = br.ReadBytes(TagSizeBytes);
                        byte[] ciphertext = br.ReadBytes((int)(fs.Length - fs.Position));

                        byte[] plaintextZip = new byte[ciphertext.Length];
                        try
                        {
                            using (var aesGcm = new AesGcm(masterKey, TagSizeBytes))
                            {
                                aesGcm.Decrypt(nonce, ciphertext, tag, plaintextZip, MagicHeaderV2);
                            }
                        }
                        catch (CryptographicException)
                        {
                            return (false, "Incorrect Master Password or authentication failed. Access Denied.");
                        }

                        File.WriteAllBytes(tempZip, plaintextZip);
                    }
                    else
                    {
                        byte[] salt = br.ReadBytes(SaltSizeBytes);
                        byte[] nonce = br.ReadBytes(NonceSizeBytes);
                        byte[] tag = br.ReadBytes(TagSizeBytes);
                        byte[] ciphertext = br.ReadBytes((int)(fs.Length - fs.Position));

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

                        File.WriteAllBytes(tempZip, plaintextZip);
                    }

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
        /// For V2 and V3 containers, unwraps the VMK directly from the container file itself, requiring ZERO database state.
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

                // For V2/V3 containers, extract VMK directly and decrypt streaming
                var (extracted, vmk, _) = ExtractVmkFromContainer(vaultFile, credential);
                if (extracted)
                {
                    return DecryptVaultWithKey(vaultFile, targetDirectory, vmk);
                }

                // If V1 legacy fallback (direct password derivation from salt)
                using var fs = new FileStream(vaultFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var br = new BinaryReader(fs, Encoding.UTF8);

                byte[] header = br.ReadBytes(MagicHeader.Length);
                if (CryptographicOperations.FixedTimeEquals(header, MagicHeader))
                {
                    byte[] salt = br.ReadBytes(SaltSizeBytes);
                    byte[] key = DeriveKey(credential, salt);
                    return DecryptVaultWithKey(vaultFile, targetDirectory, key);
                }

                return (false, "Authentication failed. Incorrect Master Password or Recovery Key.");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Probes and extracts the 256-bit Vault Master Key (VMK) directly from a container file
        /// using a candidate credential (password or recovery key).
        /// Supports self-contained V2 and V3 streaming containers.
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
                bool isV3 = CryptographicOperations.FixedTimeEquals(header, MagicHeaderV3);
                bool isV2 = CryptographicOperations.FixedTimeEquals(header, MagicHeaderV2);

                if (!isV2 && !isV3)
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
