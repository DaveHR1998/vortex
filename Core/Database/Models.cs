using System;

namespace Vortex.Core.Database
{
    public enum ProcessSafetyLevel
    {
        SystemCritical,   // Locked: e.g. csrss, lsass, smss, services, dwm
        SafeToTerminate,  // Bloatware/helpers: Adobe CC, HP support, telemetry
        DeveloperDaemon,  // Dev services: PostgreSQL, WSL vmmem, Docker (prompt before end)
        UserApplication   // Normal user apps: Chrome, Discord, Spotify
    }

    public class ProcessRule
    {
        public string ProcessName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ProcessSafetyLevel SafetyLevel { get; set; } = ProcessSafetyLevel.UserApplication;
        public bool SafeToEnd { get; set; } = false;
        public string Recommendation { get; set; } = string.Empty;
    }

    public enum CleanupSafety
    {
        SafeToDelete,
        SafeToCleanCache,
        CautionUserData,
        ProtectedSystem,
        OrphanedLeftover,
        RunningProcessLocked
    }

    public class FolderFingerprint
    {
        public string Pattern { get; set; } = string.Empty; // e.g. %APPDATA%\Code\Cache
        public string OwningApplication { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty; // Development, Browser, OEM Bloat, Media, System
        public string Description { get; set; } = string.Empty;
        public CleanupSafety Safety { get; set; } = CleanupSafety.SafeToCleanCache;
        public string Recommendation { get; set; } = string.Empty;
    }

    public class AuditEntry
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string ActionType { get; set; } = string.Empty; // MemoryBoost, ProcessKill, FileClean, RegistryBackup
        public string Target { get; set; } = string.Empty;
        public long BytesReclaimed { get; set; }
        public string Details { get; set; } = string.Empty;
    }

    public class QuarantineItem
    {
        public int Id { get; set; }
        public string OriginalPath { get; set; } = string.Empty;
        public string QuarantinePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime DeletedDate { get; set; } = DateTime.Now;
        public bool IsRestored { get; set; } = false;
    }

    public class ProcessInfoModel : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public long MemoryBytes { get; set; }
        public string MemoryString => $"{MemoryBytes / (1024.0 * 1024.0):F1} MB";
        public float CpuPercent { get; set; }
        public string CpuString => $"{CpuPercent:F1}%";
        public ProcessSafetyLevel SafetyLevel { get; set; }
        public bool CanTerminate => SafetyLevel != ProcessSafetyLevel.SystemCritical;
        public string BadgeText => SafetyLevel switch
        {
            ProcessSafetyLevel.SafeToTerminate => "🟢 Safe to End",
            ProcessSafetyLevel.DeveloperDaemon => "🟠 Dev Service",
            ProcessSafetyLevel.SystemCritical => "🔴 System Core",
            _ => "🔵 User App"
        };
        public string BadgeColor => SafetyLevel switch
        {
            ProcessSafetyLevel.SystemCritical => "#EF4444",  // Red
            ProcessSafetyLevel.SafeToTerminate => "#00E599", // Neon Mint (Proton style)
            ProcessSafetyLevel.DeveloperDaemon => "#F59E0B", // Amber
            _ => "#6D4AFF"                                  // Proton Violet
        };
        public string Recommendation { get; set; } = string.Empty;
        public int ActiveSockets { get; set; } = 0;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propName));
        }
    }

    public class FolderAttributionModel
    {
        public string Path { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
        public long TotalSizeBytes { get; set; }
        public string SizeString => TotalSizeBytes > 1024L * 1024L * 1024L 
            ? $"{TotalSizeBytes / (1024.0 * 1024.0 * 1024.0):F2} GB" 
            : $"{TotalSizeBytes / (1024.0 * 1024.0):F1} MB";
        public string OwningApplication { get; set; } = "Unknown Application";
        public string Publisher { get; set; } = "Unknown Publisher";
        public string Category { get; set; } = "System / Cache";
        public string OwnerAccount { get; set; } = "SYSTEM";
        public string Description { get; set; } = string.Empty;
        public CleanupSafety Safety { get; set; } = CleanupSafety.CautionUserData;
        public string Recommendation { get; set; } = string.Empty;
        public bool IsRunningProcessLocked { get; set; }
        public string? ActiveProcessName { get; set; }
        public int? ActiveProcessId { get; set; }
        public bool IsSelected { get; set; } = false;

        public string SafetyBadgeText => Safety switch
        {
            CleanupSafety.OrphanedLeftover => "Orphaned (Safe)",
            CleanupSafety.RunningProcessLocked => "App Running (Locked)",
            CleanupSafety.SafeToDelete => "Safe to Delete",
            CleanupSafety.SafeToCleanCache => "Safe Cache",
            CleanupSafety.CautionUserData => "User Data",
            CleanupSafety.ProtectedSystem => "Protected",
            _ => Safety.ToString()
        };

        public string SafetyBadgeColor => Safety switch
        {
            CleanupSafety.OrphanedLeftover => "#8B5CF6", // Purple Badge
            CleanupSafety.RunningProcessLocked => "#EF4444", // Red / Locked
            CleanupSafety.SafeToDelete => "#10B981", // Emerald Green
            CleanupSafety.SafeToCleanCache => "#38BDF8", // Sky Blue
            CleanupSafety.CautionUserData => "#F59E0B", // Amber Warning
            _ => "#EF4444"
        };
    }

    public class AppAccessRule
    {
        public string ProcessName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string AccessStatus { get; set; } = "ALLOWED"; // ALLOWED, BLOCKED_FIREWALL, BLOCKED_EXECUTION
        public bool MuteNotifications { get; set; } = true;
        public bool IsBuiltIn { get; set; } = false;
        public string Category { get; set; } = "General Application";
        public DateTime DateAdded { get; set; } = DateTime.Now;

        public string StatusDisplay => AccessStatus switch
        {
            "ALLOWED" => "🛡️ Allowed & Trusted",
            "BLOCKED_FIREWALL" => "🌐 Network Quarantined",
            "BLOCKED_EXECUTION" => "🚫 Execution Blacklisted",
            _ => AccessStatus
        };

        public string NotificationDisplay => MuteNotifications ? "🔇 Muted" : "🔔 Alerts Active";
    }

    public class DiscoveredAppModel
    {
        public string ProcessName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string IconGlyph { get; set; } = "💻";
        public string Category { get; set; } = "Installed Application";
        public bool IsRunning { get; set; } = false;
        public string AccessStatus { get; set; } = "UNCONFIGURED"; // UNCONFIGURED, ALLOWED, BLOCKED_FIREWALL, BLOCKED_EXECUTION
        public bool MuteNotifications { get; set; } = false; // Default to false (Alerts Active) for unconfigured apps

        public bool IsAllowed => AccessStatus == "ALLOWED";
        public bool IsBlocked => AccessStatus == "BLOCKED_FIREWALL" || AccessStatus == "BLOCKED_EXECUTION";
        public bool IsUnconfigured => AccessStatus == "UNCONFIGURED";

        public string StatusDisplay => AccessStatus switch
        {
            "ALLOWED" => "🛡️ Allowed & Trusted",
            "BLOCKED_FIREWALL" => "🌐 Network Quarantined",
            "BLOCKED_EXECUTION" => "🚫 Execution Blacklisted",
            _ => "⚪ Default Monitor"
        };

        public string StatusBadgeColor => AccessStatus switch
        {
            "ALLOWED" => "#10B981",
            "BLOCKED_FIREWALL" => "#8B5CF6",
            "BLOCKED_EXECUTION" => "#EF4444",
            _ => "#64748B"
        };

        public string RunningBadge => IsRunning ? "🟢 Running" : "⚪ Idle / Installed";
        public string NotificationDisplay => MuteNotifications ? "🔇 Muted" : "🔔 Active";
    }

    public class VaultFolderModel
    {
        public int Id { get; set; }
        public string FolderPath { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
        public string ProtectionMode { get; set; } = "LOCKED_VISIBLE"; // LOCKED_VISIBLE, STEALTH, UNLOCKED
        public bool IsLocked { get; set; } = true;
        public bool IsStealth { get; set; } = false;
        public long TotalSizeBytes { get; set; }
        public int FileCount { get; set; }
        public string EncryptedPayloadPath { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastUnlockedAt { get; set; }

        public string SizeFormatted => TotalSizeBytes > 1024L * 1024L * 1024L
            ? $"{TotalSizeBytes / (1024.0 * 1024.0 * 1024.0):F2} GB"
            : $"{TotalSizeBytes / (1024.0 * 1024.0):F1} MB";

        public string StatusDisplay => IsLocked
            ? (IsStealth ? "👻 Hidden (Stealth)" : "🔒 Locked (Password Required)")
            : "🔓 Unlocked (Active)";

        public string StatusBadgeColor => IsLocked
            ? (IsStealth ? "#8B5CF6" : "#EF4444")
            : "#10B981";

        public string ModeDisplay => IsStealth ? "Invisible (Stealth)" : "Visible (Impassable)";
        public string IconGlyph => IsLocked ? (IsStealth ? "👻" : "🔒") : "📂";
    }

    public class VaultSettingsModel
    {
        public bool IsSetup { get; set; } = false;
        public string PasswordHash { get; set; } = string.Empty;
        public string PasswordSalt { get; set; } = string.Empty;
        public string RecoveryKeyHash { get; set; } = string.Empty;
        public bool AutoLockOnScreenLock { get; set; } = true;
        public int AutoLockIdleMinutes { get; set; } = 15;
        public bool UsbWriteBlockEnabled { get; set; } = false;
        public DateTime? UsbAllowedUntil { get; set; }
        public bool ShellContextMenuEnabled { get; set; } = true;
    }

    public class RemovableDriveModel
    {
        public string DriveLetter { get; set; } = string.Empty;
        public string VolumeLabel { get; set; } = string.Empty;
        public string DriveFormat { get; set; } = string.Empty;
        public long TotalSizeBytes { get; set; }
        public long FreeSizeBytes { get; set; }
        public bool IsWriteProtected { get; set; } = true;

        public string DisplayName => string.IsNullOrEmpty(VolumeLabel) ? $"Removable Disk ({DriveLetter})" : $"{VolumeLabel} ({DriveLetter})";
        public string SizeFormatted => $"{TotalSizeBytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
        public string StatusDisplay => IsWriteProtected ? "🔒 Read-Only (Write-Blocked)" : "🟢 Write-Allowed (Temporary)";
        public string StatusBadgeColor => IsWriteProtected ? "#8B5CF6" : "#10B981";
    }
}


