using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using Microsoft.Win32;
using Vortex.Core.Database;

namespace Vortex.Core.Vault
{
    /// <summary>
    /// Service for Data Loss Prevention (DLP) controlling USB and Removable Media write-protection.
    /// Gating external file copy/paste behind Master Password authorization.
    /// </summary>
    public class UsbDlpService
    {
        private const string RegistryKeyPath = @"SYSTEM\CurrentControlSet\Control\StorageDevicePolicies";
        private const string WriteProtectValue = "WriteProtect";

        private readonly DatabaseService _db;
        private DispatcherTimer? _temporaryWindowTimer;
        public event Action<bool>? OnUsbWriteProtectChanged;
        public event Action<TimeSpan>? OnTemporaryWindowTick;
        public event Action? OnTemporaryWindowExpired;

        public UsbDlpService(DatabaseService db)
        {
            _db = db;
            InitializeTimer();
            CheckAndRestorePersistedState();
        }

        private void InitializeTimer()
        {
            _temporaryWindowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _temporaryWindowTimer.Tick += (s, e) =>
            {
                var settings = _db.GetVaultSettings();
                if (settings.UsbAllowedUntil.HasValue)
                {
                    var remaining = settings.UsbAllowedUntil.Value - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        // Time is up! Snap back to locked read-only mode
                        _temporaryWindowTimer.Stop();
                        settings.UsbAllowedUntil = null;
                        settings.UsbWriteBlockEnabled = true;
                        _db.SaveVaultSettings(settings);

                        SetRegistryWriteProtect(true);
                        OnTemporaryWindowExpired?.Invoke();
                        OnUsbWriteProtectChanged?.Invoke(true);
                    }
                    else
                    {
                        OnTemporaryWindowTick?.Invoke(remaining);
                    }
                }
                else
                {
                    _temporaryWindowTimer.Stop();
                }
            };
        }

        private void CheckAndRestorePersistedState()
        {
            try
            {
                var settings = _db.GetVaultSettings();
                if (settings.UsbWriteBlockEnabled)
                {
                    if (settings.UsbAllowedUntil.HasValue && settings.UsbAllowedUntil.Value > DateTime.UtcNow)
                    {
                        // Active temporary write window
                        SetRegistryWriteProtect(false);
                        _temporaryWindowTimer?.Start();
                    }
                    else
                    {
                        SetRegistryWriteProtect(true);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Gets current low-level registry USB write protect status.
        /// </summary>
        public bool IsUsbWriteProtectActive()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(RegistryKeyPath, false);
                if (key != null)
                {
                    object? val = key.GetValue(WriteProtectValue);
                    if (val is int intVal) return intVal == 1;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Enables or disables global USB write-blocking via Windows StorageDevicePolicies.
        /// </summary>
        public (bool success, string message) SetUsbWriteBlockEnabled(bool enabled)
        {
            try
            {
                var settings = _db.GetVaultSettings();
                settings.UsbWriteBlockEnabled = enabled;
                settings.UsbAllowedUntil = null;
                _db.SaveVaultSettings(settings);

                bool regSuccess = SetRegistryWriteProtect(enabled);
                OnUsbWriteProtectChanged?.Invoke(enabled);

                string msg = enabled
                    ? "USB & External Device Write-Protection is now ACTIVE. All removable media are locked in Read-Only mode."
                    : "USB Write-Protection is DISABLED. External drives accept standard file writes.";

                return (regSuccess, msg);
            }
            catch (Exception ex)
            {
                return (false, $"Failed to update USB policy: {ex.Message}");
            }
        }

        /// <summary>
        /// Temporarily grants write access to external drives for a specific duration (e.g. 5 min, 15 min).
        /// Automatically re-engages write-block upon timer expiration.
        /// </summary>
        public (bool success, string message) AuthorizeTemporaryWrite(TimeSpan duration)
        {
            try
            {
                var settings = _db.GetVaultSettings();
                settings.UsbAllowedUntil = DateTime.UtcNow.Add(duration);
                _db.SaveVaultSettings(settings);

                SetRegistryWriteProtect(false);
                _temporaryWindowTimer?.Start();
                OnUsbWriteProtectChanged?.Invoke(false);

                return (true, $"USB Write Access Authorized for {(int)duration.TotalMinutes} minutes. It will automatically re-lock to Read-Only when the timer expires.");
            }
            catch (Exception ex)
            {
                return (false, $"Error granting USB write window: {ex.Message}");
            }
        }

        /// <summary>
        /// Revokes any temporary write window and locks USBs back down immediately.
        /// </summary>
        public void LockUsbNow()
        {
            var settings = _db.GetVaultSettings();
            settings.UsbAllowedUntil = null;
            settings.UsbWriteBlockEnabled = true;
            _db.SaveVaultSettings(settings);

            _temporaryWindowTimer?.Stop();
            SetRegistryWriteProtect(true);
            OnUsbWriteProtectChanged?.Invoke(true);
        }

        private bool SetRegistryWriteProtect(bool protect)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(RegistryKeyPath, true);
                if (key != null)
                {
                    key.SetValue(WriteProtectValue, protect ? 1 : 0, RegistryValueKind.DWord);
                    return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Enumerates currently connected removable drives (USB thumb drives, external drives, SD cards).
        /// </summary>
        public List<RemovableDriveModel> GetConnectedRemovableDrives()
        {
            var list = new List<RemovableDriveModel>();
            bool isProtected = IsUsbWriteProtectActive();

            try
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Removable);

                foreach (var d in drives)
                {
                    if (!d.IsReady) continue;
                    list.Add(new RemovableDriveModel
                    {
                        DriveLetter = d.Name.TrimEnd('\\'),
                        VolumeLabel = string.IsNullOrEmpty(d.VolumeLabel) ? "Removable Storage" : d.VolumeLabel,
                        DriveFormat = d.DriveFormat,
                        TotalSizeBytes = d.TotalSize,
                        FreeSizeBytes = d.TotalFreeSpace,
                        IsWriteProtected = isProtected
                    });
                }
            }
            catch { }

            return list;
        }
    }
}
