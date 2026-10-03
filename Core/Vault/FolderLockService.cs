using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows.Threading;
using Microsoft.Win32;
using Vortex.Core.Database;

namespace Vortex.Core.Vault
{
    /// <summary>
    /// Core engine managing folder locking, stealth mode, access denial, and auto-lock triggers.
    /// </summary>
    public class FolderLockService
    {
        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        private readonly DatabaseService _db;
        private readonly VaultCryptoEngine _crypto;
        private readonly UsbDlpService _usbDlp;
        private DispatcherTimer? _idleCheckTimer;

        public event Action? OnVaultStateChanged;
        public event Action<string>? OnAutoLockTriggered;

        // Strictly forbidden directories to protect Windows stability
        private static readonly HashSet<string> ForbiddenSystemRoots = new(StringComparer.OrdinalIgnoreCase)
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),
            Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData"),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        };

        public FolderLockService(DatabaseService db, VaultCryptoEngine crypto, UsbDlpService usbDlp)
        {
            _db = db;
            _crypto = crypto;
            _usbDlp = usbDlp;

            InitializeAutoLockHooks();
        }

        private void InitializeAutoLockHooks()
        {
            try
            {
                // 1. Hook Windows Lock (Win+L / Screen Saver / Switch User)
                SystemEvents.SessionSwitch += (s, e) =>
                {
                    var settings = _db.GetVaultSettings();
                    if (settings.AutoLockOnScreenLock && (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.ConsoleDisconnect))
                    {
                        LockAllVaults("Windows Lock (Win+L) Triggered");
                    }
                };

                // 2. Hook Idle Inactivity Timer
                _idleCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
                _idleCheckTimer.Tick += (s, e) => CheckIdleTimeout();
                _idleCheckTimer.Start();
            }
            catch { }
        }

        private void CheckIdleTimeout()
        {
            try
            {
                var settings = _db.GetVaultSettings();
                if (settings.AutoLockIdleMinutes <= 0) return;

                var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
                if (GetLastInputInfo(ref lii))
                {
                    uint idleMs = (uint)Environment.TickCount - lii.dwTime;
                    double idleMinutes = idleMs / (1000.0 * 60.0);

                    if (idleMinutes >= settings.AutoLockIdleMinutes)
                    {
                        var unlocked = _db.GetVaultFolders().Where(f => !f.IsLocked).ToList();
                        if (unlocked.Count > 0)
                        {
                            LockAllVaults($"Idle Inactivity ({settings.AutoLockIdleMinutes}m) Triggered");
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Validates whether a folder path is safe to protect (not an OS critical directory).
        /// </summary>
        public (bool isValid, string reason) ValidateFolderPath(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return (false, "Folder path cannot be empty.");

            if (!Directory.Exists(folderPath))
                return (false, "Target directory does not exist on disk.");

            string full = Path.GetFullPath(folderPath).TrimEnd('\\');

            foreach (var forbidden in ForbiddenSystemRoots)
            {
                if (string.IsNullOrEmpty(forbidden)) continue;
                string cleanForbid = Path.GetFullPath(forbidden).TrimEnd('\\');

                if (full.Equals(cleanForbid, StringComparison.OrdinalIgnoreCase) ||
                    full.Equals(Path.GetPathRoot(full)?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    return (false, $"'{folderPath}' is a critical Windows system directory and cannot be locked for OS stability.");
                }
            }

            return (true, string.Empty);
        }

        /// <summary>
        /// Initial setup of Master Credentials (Password + Recovery Key).
        /// </summary>
        public (bool success, string plainRecoveryKey, string message) SetupMasterSecurity(string masterPassword)
        {
            if (string.IsNullOrWhiteSpace(masterPassword) || masterPassword.Length < 6)
                return (false, string.Empty, "Password must be at least 6 characters long.");

            var (hash, salt) = _crypto.HashPassword(masterPassword);
            var (plainKey, keyHash) = _crypto.GenerateRecoveryKey();

            var settings = _db.GetVaultSettings();
            settings.IsSetup = true;
            settings.PasswordHash = hash;
            settings.PasswordSalt = salt;
            settings.RecoveryKeyHash = keyHash;
            _db.SaveVaultSettings(settings);

            return (true, plainKey, "Master security configured successfully! Keep your Recovery Key in a safe location.");
        }

        /// <summary>
        /// Authenticates a password or recovery key.
        /// </summary>
        public bool Authenticate(string credential)
        {
            var settings = _db.GetVaultSettings();
            if (!settings.IsSetup) return false;

            if (_crypto.VerifyPassword(credential, settings.PasswordHash, settings.PasswordSalt))
                return true;

            if (_crypto.VerifyRecoveryKey(credential, settings.RecoveryKeyHash))
                return true;

            return false;
        }

        /// <summary>
        /// Adds a folder to the Vault and immediately locks it in either Visible or Stealth mode.
        /// </summary>
        public (bool success, string message) ProtectFolder(string folderPath, string password, bool isStealth)
        {
            var (valid, reason) = ValidateFolderPath(folderPath);
            if (!valid) return (false, reason);

            if (!Authenticate(password))
                return (false, "Authentication failed. Incorrect Master Password.");

            string full = Path.GetFullPath(folderPath).TrimEnd('\\');
            string folderName = Path.GetFileName(full);

            // Container file stored safely in %LocalAppData%\Vortex\VaultContainers\
            string containerDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vortex", "VaultContainers");
            Directory.CreateDirectory(containerDir);
            string containerFile = Path.Combine(containerDir, $"vault_{Guid.NewGuid():N}.vortexvault");

            // 1. Encrypt directory
            var (encSuccess, encError, totalBytes, fileCount) = _crypto.EncryptDirectoryToVault(full, containerFile, password);
            if (!encSuccess)
                return (false, $"Encryption failed: {encError}");

            // 2. Save model to database
            var model = new VaultFolderModel
            {
                FolderPath = full,
                FolderName = folderName,
                ProtectionMode = isStealth ? "STEALTH" : "LOCKED_VISIBLE",
                IsLocked = true,
                IsStealth = isStealth,
                TotalSizeBytes = totalBytes,
                FileCount = fileCount,
                EncryptedPayloadPath = containerFile,
                CreatedAt = DateTime.UtcNow
            };
            _db.SaveVaultFolder(model);

            // 3. Apply locking to folder on disk
            ApplyLockToDisk(model);

            OnVaultStateChanged?.Invoke();
            return (true, $"'{folderName}' is now locked and protected in {(isStealth ? "Stealth" : "Visible")} Mode.");
        }

        /// <summary>
        /// Unlocks a protected folder, decrypting and restoring files.
        /// </summary>
        public (bool success, string message) UnlockFolder(string folderPath, string password, bool openInExplorer = true)
        {
            var model = _db.GetVaultFolder(folderPath);
            if (model == null) return (false, "Folder is not registered in Vortex Vault.");

            if (!Authenticate(password))
                return (false, "Authentication failed. Incorrect Master Password.");

            if (string.IsNullOrEmpty(model.EncryptedPayloadPath) || !File.Exists(model.EncryptedPayloadPath))
                return (false, "Encrypted vault container payload is missing.");

            // 1. Clean barrier from folder if exists
            CleanBarrierFromFolder(model.FolderPath);

            // 2. Decrypt vault container back into folder
            var (decSuccess, decError) = _crypto.DecryptVaultToDirectory(model.EncryptedPayloadPath, model.FolderPath, password);
            if (!decSuccess)
                return (false, $"Decryption failed: {decError}");

            // 3. Remove Hidden / System attributes
            try
            {
                var dir = new DirectoryInfo(model.FolderPath);
                dir.Attributes &= ~FileAttributes.Hidden;
                dir.Attributes &= ~FileAttributes.System;
            }
            catch { }

            // 4. Update database
            model.IsLocked = false;
            model.ProtectionMode = "UNLOCKED";
            model.LastUnlockedAt = DateTime.UtcNow;
            _db.SaveVaultFolder(model);

            OnVaultStateChanged?.Invoke();

            if (openInExplorer && Directory.Exists(model.FolderPath))
            {
                try { System.Diagnostics.Process.Start("explorer.exe", model.FolderPath); } catch { }
            }

            return (true, $"'{model.FolderName}' unlocked successfully.");
        }

        /// <summary>
        /// Locks an existing vault folder back down.
        /// </summary>
        public (bool success, string message) LockFolder(string folderPath, string password)
        {
            var model = _db.GetVaultFolder(folderPath);
            if (model == null) return (false, "Folder is not registered in Vortex Vault.");

            if (!Authenticate(password))
                return (false, "Authentication failed. Incorrect Master Password.");

            if (Directory.Exists(model.FolderPath))
            {
                // Re-encrypt contents to container
                var (encSuccess, encError, totalBytes, fileCount) = _crypto.EncryptDirectoryToVault(model.FolderPath, model.EncryptedPayloadPath, password);
                if (encSuccess)
                {
                    model.TotalSizeBytes = totalBytes;
                    model.FileCount = fileCount;
                }
            }

            model.IsLocked = true;
            model.ProtectionMode = model.IsStealth ? "STEALTH" : "LOCKED_VISIBLE";
            _db.SaveVaultFolder(model);

            ApplyLockToDisk(model);
            OnVaultStateChanged?.Invoke();

            return (true, $"'{model.FolderName}' is now locked.");
        }

        /// <summary>
        /// Locks all active vault folders immediately.
        /// </summary>
        public void LockAllVaults(string reason = "Manual Lock")
        {
            var vaults = _db.GetVaultFolders().Where(f => !f.IsLocked).ToList();
            foreach (var v in vaults)
            {
                v.IsLocked = true;
                v.ProtectionMode = v.IsStealth ? "STEALTH" : "LOCKED_VISIBLE";
                _db.SaveVaultFolder(v);
                ApplyLockToDisk(v);
            }

            OnAutoLockTriggered?.Invoke(reason);
            OnVaultStateChanged?.Invoke();
        }

        /// <summary>
        /// Safely removes a folder from protection and restores normal unencrypted folder.
        /// </summary>
        public (bool success, string message) RemoveFromVault(string folderPath, string password)
        {
            var model = _db.GetVaultFolder(folderPath);
            if (model == null) return (false, "Folder not found in Vault.");

            if (!Authenticate(password))
                return (false, "Authentication failed. Incorrect Master Password.");

            if (model.IsLocked)
            {
                var (unlocked, msg) = UnlockFolder(folderPath, password, openInExplorer: false);
                if (!unlocked) return (false, msg);
            }

            // Clean container file
            if (!string.IsNullOrEmpty(model.EncryptedPayloadPath) && File.Exists(model.EncryptedPayloadPath))
            {
                try { File.Delete(model.EncryptedPayloadPath); } catch { }
            }

            _db.DeleteVaultFolder(folderPath);
            OnVaultStateChanged?.Invoke();

            return (true, $"'{model.FolderName}' has been removed from Vault protection and restored to normal.");
        }

        private void ApplyLockToDisk(VaultFolderModel model)
        {
            try
            {
                if (!Directory.Exists(model.FolderPath)) return;

                if (model.IsStealth)
                {
                    // Wipe directory contents from clear view (since encrypted payload exists in container)
                    WipeDirectoryFilesSafe(model.FolderPath);

                    // Apply Hidden + System attributes to make it invisible in Explorer & CMD
                    var dir = new DirectoryInfo(model.FolderPath);
                    dir.Attributes |= FileAttributes.Hidden | FileAttributes.System;
                }
                else
                {
                    // Wipe plain files and create locked barrier note
                    WipeDirectoryFilesSafe(model.FolderPath);

                    string lockNote = Path.Combine(model.FolderPath, "🔒_LOCKED_BY_VORTEX_VAULT.txt");
                    File.WriteAllText(lockNote,
                        "==========================================================\n" +
                        "           🛡️ PROTECTED BY VORTEX ZERO-TRUST VAULT        \n" +
                        "==========================================================\n\n" +
                        "This directory has been encrypted and locked with AES-256-GCM.\n" +
                        "All files are sealed inside a secure encrypted container.\n\n" +
                        "To view or modify files:\n" +
                        "1. Open Vortex System Guardian (or right-click this folder).\n" +
                        "2. Select 'Unlock with Vortex Vault'.\n" +
                        "3. Enter your Master Password.\n\n" +
                        $"Protected Folder: {model.FolderName}\n" +
                        $"Status: Sealed ({model.FileCount} files, {model.SizeFormatted})\n");

                    // Set directory icon to locked
                    var dir = new DirectoryInfo(model.FolderPath);
                    dir.Attributes &= ~FileAttributes.Hidden;
                }
            }
            catch { }
        }

        private void CleanBarrierFromFolder(string folderPath)
        {
            try
            {
                string lockNote = Path.Combine(folderPath, "🔒_LOCKED_BY_VORTEX_VAULT.txt");
                if (File.Exists(lockNote)) File.Delete(lockNote);
            }
            catch { }
        }

        private void WipeDirectoryFilesSafe(string folderPath)
        {
            try
            {
                var dir = new DirectoryInfo(folderPath);
                foreach (var file in dir.GetFiles())
                {
                    try { file.Delete(); } catch { }
                }
                foreach (var subDir in dir.GetDirectories())
                {
                    try { subDir.Delete(true); } catch { }
                }
            }
            catch { }
        }

        public List<VaultFolderModel> GetAllVaultFolders() => _db.GetVaultFolders();
        public VaultSettingsModel GetSettings() => _db.GetVaultSettings();
        public void SaveSettings(VaultSettingsModel s) => _db.SaveVaultSettings(s);
    }
}
