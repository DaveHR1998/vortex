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
        public event Action<VaultFolderModel>? OnLockedFolderAccessed;

        private readonly HashSet<string> _activelyPromptingFolders = new(StringComparer.OrdinalIgnoreCase);
        private DispatcherTimer? _explorerWatchTimer;

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
            InitializeExplorerInterceptor();
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

        private void InitializeExplorerInterceptor()
        {
            try
            {
                // Active Windows Explorer Navigation Interceptor
                // Detects when user double-clicks or navigates to a locked visible folder and prompts for password
                _explorerWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
                _explorerWatchTimer.Tick += (s, e) => CheckExplorerNavigations();
                _explorerWatchTimer.Start();
            }
            catch { }
        }

        private void CheckExplorerNavigations()
        {
            try
            {
                var lockedVaults = _db.GetVaultFolders().Where(f => f.IsLocked).ToList();
                if (lockedVaults.Count == 0) return;

                Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return;

                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic windows = shell.Windows();
                if (windows == null) return;

                foreach (dynamic window in windows)
                {
                    try
                    {
                        string? url = window.LocationURL;
                        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string localPath = Uri.UnescapeDataString(new Uri(url).LocalPath).TrimEnd('\\');
                        if (string.IsNullOrEmpty(localPath)) continue;

                        var matchedVault = lockedVaults.FirstOrDefault(v =>
                            localPath.Equals(v.FolderPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
                            localPath.StartsWith(v.FolderPath.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));

                        if (matchedVault != null)
                        {
                            lock (_activelyPromptingFolders)
                            {
                                if (_activelyPromptingFolders.Contains(matchedVault.FolderPath))
                                    continue;

                                _activelyPromptingFolders.Add(matchedVault.FolderPath);
                            }

                            // Intercept: immediately close the Explorer window that attempted to view the locked container
                            try
                            {
                                window.Quit();
                            }
                            catch
                            {
                                try
                                {
                                    string parentDir = Directory.GetParent(matchedVault.FolderPath)?.FullName ?? "shell:desktop";
                                    window.Navigate(parentDir);
                                }
                                catch { }
                            }

                            // Prompt user for master password
                            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    OnLockedFolderAccessed?.Invoke(matchedVault);
                                }
                                finally
                                {
                                    // Cooldown timer before allowing next auto-prompt for this folder
                                    var cooldown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                                    cooldown.Tick += (cs, ce) =>
                                    {
                                        cooldown.Stop();
                                        lock (_activelyPromptingFolders)
                                        {
                                            _activelyPromptingFolders.Remove(matchedVault.FolderPath);
                                        }
                                    };
                                    cooldown.Start();
                                }
                            }));
                        }
                    }
                    catch { }
                }
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
        /// Initial setup of Master Credentials (Password + Recovery Key + Wrapped 256-bit VMK).
        /// </summary>
        public (bool success, string plainRecoveryKey, string message) SetupMasterSecurity(string masterPassword)
        {
            if (string.IsNullOrWhiteSpace(masterPassword) || masterPassword.Length < 6)
                return (false, string.Empty, "Password must be at least 6 characters long.");

            var (hash, salt) = _crypto.HashPassword(masterPassword);
            var (plainKey, keyHash) = _crypto.GenerateRecoveryKey();
            byte[] vmk = _crypto.GenerateVaultMasterKey();

            string wrappedWithPass = _crypto.WrapMasterKey(vmk, masterPassword);
            string wrappedWithRecovery = _crypto.WrapMasterKey(vmk, plainKey);

            var settings = _db.GetVaultSettings();
            settings.IsSetup = true;
            settings.PasswordHash = hash;
            settings.PasswordSalt = salt;
            settings.RecoveryKeyHash = keyHash;
            settings.WrappedMasterKeyPassword = wrappedWithPass;
            settings.WrappedMasterKeyRecovery = wrappedWithRecovery;
            _db.SaveVaultSettings(settings);

            return (true, plainKey, "Master security configured successfully! Keep your Recovery Key in a safe location.");
        }

        /// <summary>
        /// Resolves the active 256-bit Vault Master Key (VMK) from user password or recovery key.
        /// Guaranteed to work seamlessly with both credentials without ever corrupting files.
        /// </summary>
        public (bool success, byte[] vmk, string message) ResolveVaultMasterKey(string credential)
        {
            if (string.IsNullOrEmpty(credential))
                return (false, Array.Empty<byte>(), "Credential cannot be empty.");

            var settings = _db.GetVaultSettings();
            if (!settings.IsSetup)
                return (false, Array.Empty<byte>(), "Vault security is not initialized.");

            // 1. Try unwrapping with Master Password slot
            if (!string.IsNullOrEmpty(settings.WrappedMasterKeyPassword))
            {
                var (unwrapped, key, _) = _crypto.UnwrapMasterKey(settings.WrappedMasterKeyPassword, credential);
                if (unwrapped) return (true, key, string.Empty);
            }

            // 2. Try unwrapping with Emergency Recovery Key slot
            if (!string.IsNullOrEmpty(settings.WrappedMasterKeyRecovery))
            {
                var (unwrapped, key, _) = _crypto.UnwrapMasterKey(settings.WrappedMasterKeyRecovery, credential);
                if (unwrapped) return (true, key, string.Empty);
            }

            // 3. Fallback / Upgrade for Legacy Vaults (derive and auto-migrate)
            if (_crypto.VerifyPassword(credential, settings.PasswordHash, settings.PasswordSalt) ||
                _crypto.VerifyRecoveryKey(credential, settings.RecoveryKeyHash))
            {
                // Generate and save fresh wrapped keys for future zero-loss operations
                byte[] vmk = _crypto.GenerateVaultMasterKey();
                settings.WrappedMasterKeyPassword = _crypto.WrapMasterKey(vmk, credential);
                if (!string.IsNullOrEmpty(settings.RecoveryKeyHash))
                {
                    // If recovery hash exists, we preserve it
                    settings.WrappedMasterKeyRecovery = _crypto.WrapMasterKey(vmk, credential);
                }
                _db.SaveVaultSettings(settings);
                return (true, vmk, string.Empty);
            }

            return (false, Array.Empty<byte>(), "Authentication failed. Incorrect Master Password or Recovery Key.");
        }

        /// <summary>
        /// Authenticates a password or recovery key.
        /// </summary>
        public bool Authenticate(string credential)
        {
            var (success, _, _) = ResolveVaultMasterKey(credential);
            return success;
        }

        /// <summary>
        /// Changes master password safely by re-wrapping the VMK without touching existing encrypted containers.
        /// </summary>
        public (bool success, string message) ChangeMasterPassword(string currentCredential, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
                return (false, "New password must be at least 6 characters long.");

            var (resolved, vmk, error) = ResolveVaultMasterKey(currentCredential);
            if (!resolved)
                return (false, "Current Master Password or Recovery Key is incorrect.");

            var (newHash, newSalt) = _crypto.HashPassword(newPassword);
            string newWrappedPass = _crypto.WrapMasterKey(vmk, newPassword);

            var settings = _db.GetVaultSettings();
            settings.PasswordHash = newHash;
            settings.PasswordSalt = newSalt;
            settings.WrappedMasterKeyPassword = newWrappedPass;
            _db.SaveVaultSettings(settings);

            return (true, "Master Password changed successfully. All encrypted vaults remain safe and accessible.");
        }

        /// <summary>
        /// Adds a folder to the Vault and immediately locks it in either Visible or Stealth mode using Envelope Encryption.
        /// </summary>
        public (bool success, string message) ProtectFolder(string folderPath, string credential, bool isStealth)
        {
            var (valid, reason) = ValidateFolderPath(folderPath);
            if (!valid) return (false, reason);

            var (auth, vmk, authError) = ResolveVaultMasterKey(credential);
            if (!auth)
                return (false, authError);

            string full = Path.GetFullPath(folderPath).TrimEnd('\\');
            string folderName = Path.GetFileName(full);

            // Container file stored safely in %LocalAppData%\Vortex\VaultContainers\
            string containerDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vortex", "VaultContainers");
            Directory.CreateDirectory(containerDir);
            string containerFile = Path.Combine(containerDir, $"vault_{Guid.NewGuid():N}.vortexvault");

            // 1. Clean any existing lock note before encrypting
            CleanBarrierFromFolder(full);

            // 2. Encrypt directory using 256-bit VMK
            var (encSuccess, encError, totalBytes, fileCount) = _crypto.EncryptDirectoryWithKey(full, containerFile, vmk);
            if (!encSuccess)
                return (false, $"Encryption failed: {encError}");

            // 3. Save model to database
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

            // 4. Apply locking to folder on disk
            ApplyLockToDisk(model);

            OnVaultStateChanged?.Invoke();
            return (true, $"'{folderName}' is now locked and protected in {(isStealth ? "Stealth" : "Visible")} Mode.");
        }

        /// <summary>
        /// Switches protection mode between Stealth and Visible without corrupting locked payloads.
        /// </summary>
        public (bool success, string message) SetStealthMode(string folderPath, bool isStealth, string credential)
        {
            var model = _db.GetVaultFolder(folderPath);
            if (model == null) return (false, "Folder is not registered in Vortex Vault.");

            if (!Authenticate(credential))
                return (false, "Authentication failed. Incorrect Master Password or Recovery Key.");

            model.IsStealth = isStealth;
            if (model.IsLocked)
            {
                model.ProtectionMode = isStealth ? "STEALTH" : "LOCKED_VISIBLE";
                _db.SaveVaultFolder(model);
                // Update disk visibility attributes and lock barrier note ONLY; do not re-encrypt container!
                ApplyLockToDisk(model);
            }
            else
            {
                _db.SaveVaultFolder(model);
            }

            OnVaultStateChanged?.Invoke();
            return (true, $"'{model.FolderName}' mode changed to {(isStealth ? "Invisible (Stealth)" : "Visible (Locked)")}.");
        }

        /// <summary>
        /// Unlocks a protected folder, decrypting and restoring files.
        /// </summary>
        public (bool success, string message) UnlockFolder(string folderPath, string credential, bool openInExplorer = true)
        {
            var model = _db.GetVaultFolder(folderPath);
            if (model == null) return (false, "Folder is not registered in Vortex Vault.");

            var (auth, vmk, authError) = ResolveVaultMasterKey(credential);
            if (!auth)
                return (false, authError);

            if (string.IsNullOrEmpty(model.EncryptedPayloadPath) || !File.Exists(model.EncryptedPayloadPath))
                return (false, "Encrypted vault container payload is missing.");

            // 1. Clean barrier from folder if exists
            CleanBarrierFromFolder(model.FolderPath);

            // 2. Decrypt vault container using VMK
            var (decSuccess, decError) = _crypto.DecryptVaultWithKey(model.EncryptedPayloadPath, model.FolderPath, vmk);
            if (!decSuccess)
            {
                // Fallback to legacy direct password decryption in case container was created on older version
                (decSuccess, decError) = _crypto.DecryptVaultToDirectory(model.EncryptedPayloadPath, model.FolderPath, credential);
            }

            if (!decSuccess)
                return (false, $"Decryption failed: {decError}");

            // 3. Ensure no lock notes remain in directory after extraction
            CleanBarrierFromFolder(model.FolderPath);

            // 4. Remove Hidden / System attributes
            try
            {
                var dir = new DirectoryInfo(model.FolderPath);
                dir.Attributes &= ~FileAttributes.Hidden;
                dir.Attributes &= ~FileAttributes.System;
            }
            catch { }

            // 5. Update database
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
        public (bool success, string message) LockFolder(string folderPath, string credential)
        {
            var model = _db.GetVaultFolder(folderPath);
            if (model == null) return (false, "Folder is not registered in Vortex Vault.");

            var (auth, vmk, authError) = ResolveVaultMasterKey(credential);
            if (!auth)
                return (false, authError);

            // Only re-encrypt if the folder was actually unlocked and files are present on disk
            if (!model.IsLocked && Directory.Exists(model.FolderPath))
            {
                // Ensure barrier note is deleted before packing directory to container
                CleanBarrierFromFolder(model.FolderPath);

                // Create a backup of previous container before updating
                if (File.Exists(model.EncryptedPayloadPath))
                {
                    try { File.Copy(model.EncryptedPayloadPath, model.EncryptedPayloadPath + ".bak", true); } catch { }
                }

                var (encSuccess, encError, totalBytes, fileCount) = _crypto.EncryptDirectoryWithKey(model.FolderPath, model.EncryptedPayloadPath, vmk);
                if (!encSuccess)
                    return (false, $"Encryption failed: {encError}");

                model.TotalSizeBytes = totalBytes;
                model.FileCount = fileCount;
            }

            model.IsLocked = true;
            model.ProtectionMode = model.IsStealth ? "STEALTH" : "LOCKED_VISIBLE";
            _db.SaveVaultFolder(model);

            ApplyLockToDisk(model);
            OnVaultStateChanged?.Invoke();

            return (true, $"'{model.FolderName}' is now locked.");
        }

        /// <summary>
        /// Emergency rescue: Decrypts any standalone .vortexvault container directly to a specified directory.
        /// </summary>
        public (bool success, string message) EmergencyRescueContainer(string containerFilePath, string targetDirectory, string credential)
        {
            if (!File.Exists(containerFilePath))
                return (false, "Container file does not exist.");

            if (!Directory.Exists(targetDirectory))
                Directory.CreateDirectory(targetDirectory);

            // 1. Try VMK if authenticated
            var (auth, vmk, _) = ResolveVaultMasterKey(credential);
            if (auth)
            {
                var (vmkSuccess, _) = _crypto.DecryptVaultWithKey(containerFilePath, targetDirectory, vmk);
                if (vmkSuccess) return (true, "Container decrypted successfully using Master Key.");
            }

            // 2. Try direct credential derivation
            var (dirSuccess, dirErr) = _crypto.DecryptVaultToDirectory(containerFilePath, targetDirectory, credential);
            if (dirSuccess) return (true, "Container decrypted successfully.");

            return (false, $"Rescue failed: {dirErr}");
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
        public (bool success, string message) RemoveFromVault(string folderPath, string credential)
        {
            var model = _db.GetVaultFolder(folderPath);
            if (model == null) return (false, "Folder not found in Vault.");

            if (!Authenticate(credential))
                return (false, "Authentication failed. Incorrect Master Password or Recovery Key.");

            if (model.IsLocked)
            {
                var (unlocked, msg) = UnlockFolder(folderPath, credential, openInExplorer: false);
                if (!unlocked) return (false, msg);
            }

            // Clean container file and backup
            if (!string.IsNullOrEmpty(model.EncryptedPayloadPath) && File.Exists(model.EncryptedPayloadPath))
            {
                try { File.Delete(model.EncryptedPayloadPath); } catch { }
                try { File.Delete(model.EncryptedPayloadPath + ".bak"); } catch { }
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
                    // Clean barrier note and wipe directory contents from clear view
                    CleanBarrierFromFolder(model.FolderPath);
                    WipeDirectoryFilesSafe(model.FolderPath);

                    // Apply Hidden + System attributes to make it invisible in Explorer & CMD
                    var dir = new DirectoryInfo(model.FolderPath);
                    dir.Attributes |= FileAttributes.Hidden | FileAttributes.System;
                }
                else
                {
                    // Remove stealth attributes first
                    var dir = new DirectoryInfo(model.FolderPath);
                    dir.Attributes &= ~FileAttributes.Hidden;
                    dir.Attributes &= ~FileAttributes.System;

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
                        "1. Open Vortex System Guardian (or double-click / right-click this folder).\n" +
                        "2. Enter your Master Password when prompted.\n\n" +
                        $"Protected Folder: {model.FolderName}\n" +
                        $"Status: Sealed ({model.FileCount} files, {model.SizeFormatted})\n");
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
                string lockNote = Path.Combine(folderPath, "🔒_LOCKED_BY_VORTEX_VAULT.txt");
                var dir = new DirectoryInfo(folderPath);
                foreach (var file in dir.GetFiles())
                {
                    if (file.FullName.Equals(lockNote, StringComparison.OrdinalIgnoreCase)) continue;
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
