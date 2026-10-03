using System.Collections.Generic;

namespace Vortex.Core.SystemInfo
{
    // ─── System Info Models ───────────────────────────────────────────────────

    public class HardwareSpecItem
    {
        public string Property { get; set; } = string.Empty;
        public string Value    { get; set; } = string.Empty;
    }

    public class LiveSensorReading
    {
        public float  CpuTempC       { get; set; }
        public float  CpuUsagePct    { get; set; }
        public float  GpuTempC       { get; set; }
        public float  GpuUsagePct    { get; set; }
        public float  GpuVramUsedMb  { get; set; }
        public float  DiskTempC      { get; set; }
        public float  MbTempC        { get; set; }
        public float  RamUsagePct    { get; set; }
        public long   RamUsedBytes   { get; set; }
        public long   RamTotalBytes  { get; set; }

        // Temperature status helpers
        public string CpuTempColor  => CpuTempC  >= 95 ? "#EF4444" : CpuTempC  >= 85 ? "#F59E0B" : "#00E599";
        public string GpuTempColor  => GpuTempC  >= 90 ? "#EF4444" : GpuTempC  >= 80 ? "#F59E0B" : "#00E599";
        public string DiskTempColor => DiskTempC >= 60 ? "#EF4444" : DiskTempC >= 50 ? "#F59E0B" : "#00E599";
        public string MbTempColor   => MbTempC   >= 80 ? "#EF4444" : MbTempC   >= 70 ? "#F59E0B" : "#00E599";

        public bool IsCpuWarning  => CpuTempC  >= 85;
        public bool IsGpuWarning  => GpuTempC  >= 80;
        public bool IsDiskWarning => DiskTempC >= 50;
        public bool IsMbWarning   => MbTempC   >= 70;

        public bool AnyCritical   => CpuTempC >= 95 || GpuTempC >= 90 || DiskTempC >= 60 || MbTempC >= 80;
        public bool AnyWarning    => IsCpuWarning || IsGpuWarning || IsDiskWarning || IsMbWarning;
    }

    public enum SystemInfoTab { Cpu, Gpu, Memory, Storage, Motherboard, OsAndSoftware }
}
