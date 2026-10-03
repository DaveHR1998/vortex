using System.Collections.Generic;

namespace Vortex.Core.Privacy
{
    public enum PrivacyRiskLevel { Low, Medium, High, Critical }

    // ─── Privacy Eraser Model ─────────────────────────────────────────────────

    public class PrivacyTraceItem
    {
        public string          Name        { get; set; } = string.Empty;
        public string          Description { get; set; } = string.Empty;
        public string          Location    { get; set; } = string.Empty;
        public long            SizeBytes   { get; set; }
        public int             ItemCount   { get; set; }
        public PrivacyRiskLevel Risk       { get; set; } = PrivacyRiskLevel.Low;
        public bool            IsSelected  { get; set; } = true;
        public string SizeString => SizeBytes > 0
            ? (SizeBytes > 1024 * 1024 ? $"{SizeBytes / (1024.0 * 1024):F1} MB" : $"{SizeBytes / 1024.0:F0} KB")
            : "—";
        public string RiskBadgeText => Risk switch
        {
            PrivacyRiskLevel.Critical => "🔴 CRITICAL",
            PrivacyRiskLevel.High     => "🟠 HIGH",
            PrivacyRiskLevel.Medium   => "🟡 MEDIUM",
            _                         => "🟢 LOW",
        };
        public string RiskBadgeColor => Risk switch
        {
            PrivacyRiskLevel.Critical => "#7F1D1D",
            PrivacyRiskLevel.High     => "#78350F",
            PrivacyRiskLevel.Medium   => "#713F12",
            _                         => "#14532D",
        };
    }

    // ─── Identity Shield Model ────────────────────────────────────────────────

    public class IdentityFinding
    {
        public string          Category    { get; set; } = string.Empty;
        public string          Name        { get; set; } = string.Empty;
        public string          Detail      { get; set; } = string.Empty;
        public string          Location    { get; set; } = string.Empty;
        public PrivacyRiskLevel Risk       { get; set; } = PrivacyRiskLevel.Low;
        public string          ActionLabel { get; set; } = "View";
        public string          ActionTarget{ get; set; } = string.Empty; // shell target or command

        public string RiskBadgeText => Risk switch
        {
            PrivacyRiskLevel.Critical => "🔴 CRITICAL",
            PrivacyRiskLevel.High     => "🟠 HIGH",
            PrivacyRiskLevel.Medium   => "🟡 MEDIUM",
            _                         => "🟢 LOW",
        };
        public string RiskBadgeColor => Risk switch
        {
            PrivacyRiskLevel.Critical => "#7F1D1D",
            PrivacyRiskLevel.High     => "#78350F",
            PrivacyRiskLevel.Medium   => "#713F12",
            _                         => "#14532D",
        };
    }

    // ─── Winget Package Model ─────────────────────────────────────────────────

    public class WingetPackage : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public string Name       { get; set; } = string.Empty;
        public string Id         { get; set; } = string.Empty;
        public string CurrentVer { get; set; } = string.Empty;
        public string LatestVer  { get; set; } = string.Empty;
        public string Source     { get; set; } = string.Empty;
        public bool   IsUnknown  { get; set; }

        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        public string BadgeText  => IsUnknown ? "⚠ UNKNOWN" : string.Empty;
        public string BadgeColor => IsUnknown ? "#78350F"   : "Transparent";
    }
}
