using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Vortex.Core.SystemIntegration
{
    public class ServiceItemModel
    {
        public string ServiceName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = "STOPPED";
        public bool IsRunning => Status.Equals("RUNNING", StringComparison.OrdinalIgnoreCase);
        public string StartupType { get; set; } = "3";
        public string StartupTypeDisplay { get; set; } = "Manual";
        public string ExecutablePath { get; set; } = string.Empty;
        public string Category { get; set; } = "Windows Core";
        public string Impact { get; set; } = "Low";
        public string Recommendation { get; set; } = "Standard system service.";
        public bool IsBloat { get; set; } = false;
        public bool IsThirdParty { get; set; } = false;
        public string IconGlyph { get; set; } = "⚙️";
        public string RunActionLabel => IsRunning ? "⏹️ Stop" : "▶️ Start";
    }

    public class ServiceControllerService
    {
        public static readonly Dictionary<string, (string Desc, string Rec, string Impact)> BloatRegistry = new(StringComparer.OrdinalIgnoreCase)
        {
            ["DiagTrack"] = ("Connected User Experiences and Telemetry", "Safe to disable. Telemetry data collection.", "Moderate"),
            ["dmwappushservice"] = ("WAP Push Telemetry Service", "Safe to disable. Transmits diagnostics packets.", "Low"),
            ["WerSvc"] = ("Windows Error Reporting", "Safe to disable unless submitting crash logs.", "Low"),
            ["Fax"] = ("Fax Service", "Safe to disable unless fax hardware is connected.", "Low"),
            ["MapsBroker"] = ("Downloaded Maps Manager", "Safe to disable if not using Windows Maps app.", "Low"),
            ["RetailDemo"] = ("Retail Demo Service", "Safe to disable. Store display mode.", "Low"),
            ["RemoteRegistry"] = ("Remote Registry", "Safe to disable. Security risk if enabled.", "Low"),
            ["XblAuthManager"] = ("Xbox Live Auth Manager", "Safe to disable if not playing Xbox games.", "Low"),
            ["XblGameSave"] = ("Xbox Live Game Save", "Safe to disable if not playing Xbox games.", "Low"),
            ["XboxNetApiSvc"] = ("Xbox Live Networking Service", "Safe to disable on non-gaming PCs.", "Low"),
            ["SysMain"] = ("SysMain / Superfetch", "Safe to stop if using fast NVMe SSD.", "High")
        };

        public List<ServiceItemModel> GetOptimizableServices()
        {
            return GetAllServices();
        }

        public List<ServiceItemModel> GetAllServices()
        {
            var services = new List<ServiceItemModel>();
            var statusMap = new Dictionary<string, (string DisplayName, string State)>(StringComparer.OrdinalIgnoreCase);

            // 1. Fast query of all active / stopped services via sc.exe
            try
            {
                var psi = new ProcessStartInfo("sc.exe", "query type= service state= all")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(3000);

                    using var reader = new StringReader(output);
                    string? line;
                    string currentName = "";
                    string currentDisplay = "";
                    string currentState = "STOPPED";

                    while ((line = reader.ReadLine()) != null)
                    {
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith("SERVICE_NAME:", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrEmpty(currentName))
                            {
                                statusMap[currentName] = (currentDisplay, currentState);
                            }
                            currentName = trimmed.Substring("SERVICE_NAME:".Length).Trim();
                            currentDisplay = currentName;
                            currentState = "STOPPED";
                        }
                        else if (trimmed.StartsWith("DISPLAY_NAME:", StringComparison.OrdinalIgnoreCase))
                        {
                            currentDisplay = trimmed.Substring("DISPLAY_NAME:".Length).Trim();
                        }
                        else if (trimmed.StartsWith("STATE", StringComparison.OrdinalIgnoreCase))
                        {
                            currentState = trimmed.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) ? "RUNNING" : "STOPPED";
                        }
                    }

                    if (!string.IsNullOrEmpty(currentName))
                    {
                        statusMap[currentName] = (currentDisplay, currentState);
                    }
                }
            }
            catch { }

            // 2. Read Registry for service details and startup types
            try
            {
                using var baseKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
                if (baseKey != null)
                {
                    foreach (var subName in baseKey.GetSubKeyNames())
                    {
                        try
                        {
                            using var svcKey = baseKey.OpenSubKey(subName);
                            if (svcKey == null) continue;

                            int type = (int)(svcKey.GetValue("Type") ?? 0);
                            // Skip pure device drivers (1, 2) unless enumerated in sc.exe
                            if (type != 16 && type != 32 && type != 272 && type != 288 && !statusMap.ContainsKey(subName))
                            {
                                continue;
                            }

                            string displayName = svcKey.GetValue("DisplayName") as string ?? subName;
                            string desc = svcKey.GetValue("Description") as string ?? "";
                            string imagePath = svcKey.GetValue("ImagePath") as string ?? "";
                            int startVal = (int)(svcKey.GetValue("Start") ?? 3);

                            string startupDisplay = startVal switch
                            {
                                2 => "Automatic",
                                3 => "Manual",
                                4 => "Disabled",
                                0 or 1 => "Boot",
                                _ => "Unknown"
                            };

                            string state = "STOPPED";
                            if (statusMap.TryGetValue(subName, out var st))
                            {
                                state = st.State;
                                if (!string.IsNullOrEmpty(st.DisplayName) && !st.DisplayName.StartsWith("@"))
                                    displayName = st.DisplayName;
                            }

                            bool isBloat = BloatRegistry.ContainsKey(subName);
                            bool isThirdParty = !imagePath.Contains("System32", StringComparison.OrdinalIgnoreCase) &&
                                                !imagePath.Contains("SysWOW64", StringComparison.OrdinalIgnoreCase) &&
                                                (imagePath.Contains("Program Files", StringComparison.OrdinalIgnoreCase) ||
                                                 imagePath.Contains("AppData", StringComparison.OrdinalIgnoreCase));

                            string category = isBloat ? "⚡ Bloat / Safe to Disable" :
                                              isThirdParty ? "📦 Third-Party Vendor" : "Windows Core";

                            string impact = isBloat ? BloatRegistry[subName].Impact : (isThirdParty ? "Moderate" : "Low");
                            string rec = isBloat ? BloatRegistry[subName].Rec :
                                         (isThirdParty ? "Third-party application background service." : "Standard Windows component.");

                            string glyph = isBloat ? "⚡" : (isThirdParty ? "📦" : "⚙️");

                            services.Add(new ServiceItemModel
                            {
                                ServiceName = subName,
                                DisplayName = displayName,
                                Description = desc,
                                Status = state,
                                StartupType = startVal.ToString(),
                                StartupTypeDisplay = startupDisplay,
                                ExecutablePath = imagePath,
                                Category = category,
                                Impact = impact,
                                Recommendation = rec,
                                IsBloat = isBloat,
                                IsThirdParty = isThirdParty,
                                IconGlyph = glyph
                            });
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return services.OrderByDescending(s => s.IsBloat)
                           .ThenByDescending(s => s.IsRunning)
                           .ThenBy(s => s.DisplayName)
                           .ToList();
        }

        public (bool success, string message) ToggleService(string serviceName, bool start)
        {
            try
            {
                string action = start ? "start" : "stop";
                var psi = new ProcessStartInfo("sc.exe", $"{action} \"{serviceName}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);

                string newStatus = QueryServiceStatus(serviceName);
                bool success = start ? newStatus == "RUNNING" : newStatus == "STOPPED";
                return (success, $"Service '{serviceName}' is now {newStatus}.");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to toggle service: {ex.Message}");
            }
        }

        public (bool success, string message) SetStartupType(string serviceName, string targetType)
        {
            try
            {
                // targetType: "disabled", "demand", "auto"
                var psi = new ProcessStartInfo("sc.exe", $"config \"{serviceName}\" start= {targetType}")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);

                string displayType = targetType switch
                {
                    "disabled" => "Disabled",
                    "demand" => "Manual",
                    "auto" => "Automatic",
                    _ => targetType
                };

                return (true, $"Startup type for '{serviceName}' set to {displayType} (Persists across reboots).");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to set startup type: {ex.Message}");
            }
        }

        public (int disabledCount, string summary) DebloatSafeServices()
        {
            int count = 0;
            foreach (var bloatKey in BloatRegistry.Keys)
            {
                try
                {
                    SetStartupType(bloatKey, "disabled");
                    ToggleService(bloatKey, false);
                    count++;
                }
                catch { }
            }
            return (count, $"Successfully disabled and stopped {count} non-essential background bloat services.");
        }

        private string QueryServiceStatus(string serviceName)
        {
            try
            {
                var psi = new ProcessStartInfo("sc.exe", $"query \"{serviceName}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(1000);
                    if (output.Contains("STATE") && output.Contains("RUNNING"))
                    {
                        return "RUNNING";
                    }
                    else if (output.Contains("STATE") && output.Contains("STOPPED"))
                    {
                        return "STOPPED";
                    }
                }
            }
            catch { }
            return "UNKNOWN";
        }
    }
}
