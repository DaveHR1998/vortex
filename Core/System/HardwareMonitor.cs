using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using LibreHardwareMonitor.Hardware;
using Vortex.Core.SystemInfo;

namespace Vortex.Core.SystemIntegration
{
    /// <summary>
    /// Polls LibreHardwareMonitor for real-time CPU/GPU/Disk/Motherboard temperatures
    /// and usage sensors. Also provides full hardware spec tables via WMI.
    /// </summary>
    public class HardwareMonitor : IDisposable
    {
        private readonly PerformanceCounterWrapper _cpuCounter;
        private readonly Computer              _computer;
        private bool _disposed;

        public HardwareMonitor()
        {
            // CPU usage counter (existing)
            _cpuCounter = new PerformanceCounterWrapper();

            // LibreHardwareMonitor
            _computer = new Computer
            {
                IsCpuEnabled         = true,
                IsGpuEnabled         = true,
                IsMemoryEnabled      = true,
                IsMotherboardEnabled = true,
                IsStorageEnabled     = true
            };

            try { _computer.Open(); } catch { /* sensor access denied — run without temps */ }
        }

        // ─── Existing methods (unchanged) ────────────────────────────────────

        public float GetCpuUsagePercentage() => _cpuCounter.GetValue();

        public (long totalBytes, long freeBytes, double usedPercentage) GetDriveStorage(string driveLetter = "C")
        {
            try
            {
                var drive = new System.IO.DriveInfo(driveLetter);
                if (drive.IsReady)
                {
                    long total = drive.TotalSize;
                    long free  = drive.AvailableFreeSpace;
                    return (total, free, ((double)(total - free) / total) * 100.0);
                }
            }
            catch { }
            return (512L * 1024 * 1024 * 1024, 180L * 1024 * 1024 * 1024, 65.0);
        }

        // ─── New: Live Sensor Reading ─────────────────────────────────────────

        public LiveSensorReading GetLiveReading()
        {
            var reading = new LiveSensorReading
            {
                CpuUsagePct  = GetCpuUsagePercentage(),
                RamUsagePct  = GetRamUsagePercent(out long used, out long total),
                RamUsedBytes = used,
                RamTotalBytes = total
            };

            try
            {
                foreach (var hw in _computer.Hardware)
                {
                    hw.Update();

                    switch (hw.HardwareType)
                    {
                        case HardwareType.Cpu:
                            foreach (var s in hw.Sensors)
                            {
                                if (s.SensorType == SensorType.Temperature && s.Value.HasValue)
                                    reading.CpuTempC = Math.Max(reading.CpuTempC, s.Value.Value);
                                if (s.SensorType == SensorType.Load && s.Name.Contains("Total") && s.Value.HasValue)
                                    reading.CpuUsagePct = s.Value.Value;
                            }
                            foreach (var sub in hw.SubHardware) { sub.Update(); foreach (var s in sub.Sensors) { if (s.SensorType == SensorType.Temperature && s.Value.HasValue) reading.CpuTempC = Math.Max(reading.CpuTempC, s.Value.Value); } }
                            break;

                        case HardwareType.GpuNvidia:
                        case HardwareType.GpuAmd:
                        case HardwareType.GpuIntel:
                            foreach (var s in hw.Sensors)
                            {
                                if (s.SensorType == SensorType.Temperature && s.Value.HasValue)
                                    reading.GpuTempC = Math.Max(reading.GpuTempC, s.Value.Value);
                                if (s.SensorType == SensorType.Load && s.Name.Contains("Core") && s.Value.HasValue)
                                    reading.GpuUsagePct = s.Value.Value;
                                if (s.SensorType == SensorType.SmallData && s.Name.Contains("Memory Used") && s.Value.HasValue)
                                    reading.GpuVramUsedMb = s.Value.Value;
                            }
                            break;

                        case HardwareType.Motherboard:
                            foreach (var sub in hw.SubHardware)
                            {
                                sub.Update();
                                foreach (var s in sub.Sensors)
                                {
                                    if (s.SensorType == SensorType.Temperature && s.Value.HasValue)
                                        reading.MbTempC = Math.Max(reading.MbTempC, s.Value.Value);
                                }
                            }
                            break;

                        case HardwareType.Storage:
                            foreach (var s in hw.Sensors)
                            {
                                if (s.SensorType == SensorType.Temperature && s.Value.HasValue)
                                    reading.DiskTempC = Math.Max(reading.DiskTempC, s.Value.Value);
                            }
                            break;
                    }
                }
            }
            catch { /* sensor read failed — return partial reading */ }

            return reading;
        }

        private static float GetRamUsagePercent(out long usedBytes, out long totalBytes)
        {
            usedBytes = 0; totalBytes = 1;
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                foreach (ManagementObject obj in searcher.Get())
                {
                    totalBytes = Convert.ToInt64(obj["TotalVisibleMemorySize"]) * 1024L;
                    long free  = Convert.ToInt64(obj["FreePhysicalMemory"])    * 1024L;
                    usedBytes  = totalBytes - free;
                    return (float)((double)usedBytes / totalBytes * 100.0);
                }
            }
            catch { }
            return 0f;
        }

        // ─── Hardware Spec Tables (WMI) ───────────────────────────────────────

        public List<HardwareSpecItem> GetCpuSpecs()
        {
            var list = new List<HardwareSpecItem>();
            try
            {
                using var s = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
                foreach (ManagementObject o in s.Get())
                {
                    Add(list, "Processor Name",       o["Name"]);
                    Add(list, "Manufacturer",         o["Manufacturer"]);
                    Add(list, "Architecture",         ArchStr(o["Architecture"]));
                    Add(list, "Physical Cores",       o["NumberOfCores"]);
                    Add(list, "Logical Processors",   o["NumberOfLogicalProcessors"]);
                    Add(list, "Base Clock",           $"{Convert.ToDouble(o["MaxClockSpeed"]) / 1000.0:F2} GHz");
                    Add(list, "L2 Cache",             FormatKb(o["L2CacheSize"]));
                    Add(list, "L3 Cache",             FormatKb(o["L3CacheSize"]));
                    Add(list, "Socket",               o["SocketDesignation"]);
                    Add(list, "Stepping",             o["Stepping"]);
                    break;
                }
            }
            catch { }
            return list;
        }

        public List<HardwareSpecItem> GetGpuSpecs()
        {
            var list = new List<HardwareSpecItem>();
            try
            {
                using var s = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                foreach (ManagementObject o in s.Get())
                {
                    Add(list, "GPU Name",         o["Name"]);
                    Add(list, "Adapter RAM",      FormatBytes(Convert.ToInt64(o["AdapterRAM"] ?? 0L)));
                    Add(list, "Driver Version",   o["DriverVersion"]);
                    Add(list, "Driver Date",      o["DriverDate"]);
                    Add(list, "Current Res.",     $"{o["CurrentHorizontalResolution"]} x {o["CurrentVerticalResolution"]}");
                    Add(list, "Refresh Rate",     $"{o["CurrentRefreshRate"]} Hz");
                    Add(list, "Video Mode",       o["VideoModeDescription"]);
                    Add(list, "DAC Type",         o["AdapterDACType"]);
                    list.Add(new HardwareSpecItem { Property = string.Empty, Value = string.Empty }); // separator
                }
            }
            catch { }
            return list;
        }

        public List<HardwareSpecItem> GetMemorySpecs()
        {
            var list = new List<HardwareSpecItem>();
            try
            {
                int slot = 0;
                using var s = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory");
                foreach (ManagementObject o in s.Get())
                {
                    slot++;
                    Add(list, $"Slot {slot} — Manufacturer",  o["Manufacturer"]);
                    Add(list, $"Slot {slot} — Capacity",      FormatBytes(Convert.ToInt64(o["Capacity"] ?? 0L)));
                    Add(list, $"Slot {slot} — Speed",         $"{o["Speed"]} MHz");
                    Add(list, $"Slot {slot} — Type",          MemTypeStr(o["MemoryType"]));
                    Add(list, $"Slot {slot} — Form Factor",   FormFactorStr(o["FormFactor"]));
                    Add(list, $"Slot {slot} — Part Number",   o["PartNumber"]?.ToString()?.Trim());
                    list.Add(new HardwareSpecItem { Property = string.Empty, Value = string.Empty });
                }
            }
            catch { }
            return list;
        }

        public List<HardwareSpecItem> GetStorageSpecs()
        {
            var list = new List<HardwareSpecItem>();
            try
            {
                using var s = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
                int idx = 0;
                foreach (ManagementObject o in s.Get())
                {
                    idx++;
                    Add(list, $"Drive {idx} — Model",      o["Model"]);
                    Add(list, $"Drive {idx} — Size",       FormatBytes(Convert.ToInt64(o["Size"] ?? 0L)));
                    Add(list, $"Drive {idx} — Interface",  o["InterfaceType"]);
                    Add(list, $"Drive {idx} — Partitions", o["Partitions"]);
                    Add(list, $"Drive {idx} — Serial",     o["SerialNumber"]?.ToString()?.Trim());
                    list.Add(new HardwareSpecItem { Property = string.Empty, Value = string.Empty });
                }
            }
            catch { }
            return list;
        }

        public List<HardwareSpecItem> GetMotherboardSpecs()
        {
            var list = new List<HardwareSpecItem>();
            try
            {
                using var sb = new ManagementObjectSearcher("SELECT * FROM Win32_BaseBoard");
                foreach (ManagementObject o in sb.Get())
                {
                    Add(list, "Manufacturer",   o["Manufacturer"]);
                    Add(list, "Product",        o["Product"]);
                    Add(list, "Version",        o["Version"]);
                    Add(list, "Serial Number",  o["SerialNumber"]);
                    break;
                }
                using var bios = new ManagementObjectSearcher("SELECT * FROM Win32_BIOS");
                foreach (ManagementObject o in bios.Get())
                {
                    Add(list, "BIOS Manufacturer",  o["Manufacturer"]);
                    Add(list, "BIOS Version",        o["SMBIOSBIOSVersion"]);
                    Add(list, "BIOS Release Date",   o["ReleaseDate"]);
                    break;
                }
            }
            catch { }
            return list;
        }

        public List<HardwareSpecItem> GetOsAndSoftwareSpecs()
        {
            var list = new List<HardwareSpecItem>();
            try
            {
                using var os = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
                foreach (ManagementObject o in os.Get())
                {
                    Add(list, "OS Name",         o["Caption"]);
                    Add(list, "Version",         o["Version"]);
                    Add(list, "Build Number",    o["BuildNumber"]);
                    Add(list, "Architecture",    o["OSArchitecture"]);
                    Add(list, "Install Date",    WmiDateStr(o["InstallDate"]?.ToString()));
                    Add(list, "Last Boot",       WmiDateStr(o["LastBootUpTime"]?.ToString()));
                    Add(list, "System Drive",    o["SystemDrive"]);
                    Add(list, "Windows Dir",     o["WindowsDirectory"]);
                    break;
                }

                list.Add(new HardwareSpecItem { Property = "── Installed Software ──", Value = string.Empty });

                using var uninstall = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall != null)
                {
                    foreach (var subKeyName in uninstall.GetSubKeyNames())
                    {
                        using var sub = uninstall.OpenSubKey(subKeyName);
                        if (sub == null) continue;
                        var name = sub.GetValue("DisplayName")?.ToString();
                        var ver  = sub.GetValue("DisplayVersion")?.ToString();
                        if (!string.IsNullOrWhiteSpace(name))
                            Add(list, name, string.IsNullOrWhiteSpace(ver) ? "—" : ver);
                    }
                }
            }
            catch { }
            return list;
        }

        // ─── Export ───────────────────────────────────────────────────────────

        public string GenerateTxtReport(
            List<HardwareSpecItem> cpu,
            List<HardwareSpecItem> gpu,
            List<HardwareSpecItem> mem,
            List<HardwareSpecItem> storage,
            List<HardwareSpecItem> mb,
            List<HardwareSpecItem> os,
            LiveSensorReading      live)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("==========================================================");
            sb.AppendLine("         VORTEX SYSTEM INFORMATION REPORT");
            sb.AppendLine($"         Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("==========================================================");
            sb.AppendLine();
            AppendSection(sb, "LIVE SENSOR READINGS", new List<HardwareSpecItem>
            {
                new() { Property = "CPU Temperature",  Value = live.CpuTempC  > 0 ? $"{live.CpuTempC:F1} °C"  : "N/A" },
                new() { Property = "CPU Usage",        Value = $"{live.CpuUsagePct:F1} %" },
                new() { Property = "GPU Temperature",  Value = live.GpuTempC  > 0 ? $"{live.GpuTempC:F1} °C"  : "N/A" },
                new() { Property = "GPU Usage",        Value = $"{live.GpuUsagePct:F1} %" },
                new() { Property = "Disk Temperature", Value = live.DiskTempC > 0 ? $"{live.DiskTempC:F1} °C" : "N/A" },
                new() { Property = "MB Temperature",   Value = live.MbTempC   > 0 ? $"{live.MbTempC:F1} °C"   : "N/A" },
                new() { Property = "RAM Usage",        Value = $"{live.RamUsagePct:F1} % ({FormatBytes(live.RamUsedBytes)} / {FormatBytes(live.RamTotalBytes)})" },
            });
            AppendSection(sb, "PROCESSOR (CPU)",      cpu);
            AppendSection(sb, "GRAPHICS (GPU)",       gpu);
            AppendSection(sb, "MEMORY (RAM)",         mem);
            AppendSection(sb, "STORAGE",              storage);
            AppendSection(sb, "MOTHERBOARD & BIOS",   mb);
            AppendSection(sb, "OPERATING SYSTEM & SOFTWARE", os);
            return sb.ToString();
        }

        private static void AppendSection(StringBuilder sb, string title, List<HardwareSpecItem> items)
        {
            sb.AppendLine($"── {title} ──────────────────────────────────");
            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item.Property)) { sb.AppendLine(); continue; }
                sb.AppendLine($"  {item.Property,-35} {item.Value}");
            }
            sb.AppendLine();
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private static void Add(List<HardwareSpecItem> list, string prop, object? val)
        {
            string v = val?.ToString()?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(v))
                list.Add(new HardwareSpecItem { Property = prop, Value = v });
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "N/A";
            if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
            if (bytes >= 1024L * 1024)        return $"{bytes / (1024.0 * 1024):F0} MB";
            return $"{bytes / 1024.0:F0} KB";
        }

        private static string FormatKb(object? kb)
        {
            if (kb == null) return "N/A";
            long v = Convert.ToInt64(kb) * 1024L;
            return FormatBytes(v);
        }

        private static string ArchStr(object? arch) => Convert.ToInt32(arch ?? 0) switch
        {
            0  => "x86 (32-bit)", 5 => "ARM", 9 => "x64 (64-bit)",
            12 => "ARM64",        _ => arch?.ToString() ?? "Unknown"
        };

        private static string MemTypeStr(object? t) => Convert.ToInt32(t ?? 0) switch
        {
            20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", 34 => "DDR5", _ => "Unknown"
        };

        private static string FormFactorStr(object? f) => Convert.ToInt32(f ?? 0) switch
        {
            8 => "DIMM", 12 => "SO-DIMM", 13 => "SO-DIMM", _ => f?.ToString() ?? "Unknown"
        };

        private static string WmiDateStr(string? wmi)
        {
            if (string.IsNullOrEmpty(wmi) || wmi.Length < 8) return "N/A";
            try { return DateTime.ParseExact(wmi[..14], "yyyyMMddHHmmss", null).ToString("yyyy-MM-dd HH:mm"); }
            catch { return wmi; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _computer.Close(); } catch { }
        }
    }

    // Thin wrapper so the constructor doesn't throw if perf counters are unavailable
    internal class PerformanceCounterWrapper
    {
        private System.Diagnostics.PerformanceCounter? _counter;
        public PerformanceCounterWrapper()
        {
            try { _counter = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total"); _counter.NextValue(); }
            catch { _counter = null; }
        }
        public float GetValue() { try { return _counter?.NextValue() ?? 0f; } catch { return 0f; } }
    }
}
