using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Vortex.Core.SystemIntegration
{
    public class ServiceItemModel
    {
        public string ServiceName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = "Stopped";
        public bool IsRunning => Status.Equals("RUNNING", StringComparison.OrdinalIgnoreCase);
        public string Impact { get; set; } = "Low";
        public string Recommendation { get; set; } = "Safe to disable if unused.";
    }

    public class ServiceControllerService
    {
        private static readonly List<(string Name, string Display, string Desc, string Impact, string Rec)> KnownServices = new()
        {
            ("DiagTrack", "Connected User Experiences and Telemetry", "Collects and uploads Windows diagnostic and usage metrics to Microsoft.", "Moderate", "Safe to stop to prevent background disk and network activity."),
            ("WerSvc", "Windows Error Reporting Service", "Captures and uploads application error logs and crash dumps.", "Low", "Safe to stop unless debugging Windows system crashes."),
            ("XblAuthManager", "Xbox Live Auth Manager", "Provides authentication for Xbox Live network features.", "Low", "Safe to stop on work workstations without Xbox gaming."),
            ("XblGameSave", "Xbox Live Game Save", "Syncs cloud save data for Xbox games.", "Low", "Safe to disable if not playing Xbox titles."),
            ("Fax", "Fax Service", "Enables sending and receiving faxes on this machine.", "Low", "Safe to stop if no fax hardware is installed."),
            ("MapsBroker", "Downloaded Maps Manager", "Manages downloaded offline maps for Windows Maps app.", "Low", "Safe to stop unless using offline Windows Maps."),
            ("SysMain", "SysMain (Superfetch)", "Preloads frequently used applications into RAM over time.", "High", "Can consume substantial background RAM and disk I/O on SSD systems.")
        };

        public List<ServiceItemModel> GetOptimizableServices()
        {
            var list = new List<ServiceItemModel>();

            foreach (var s in KnownServices)
            {
                string status = QueryServiceStatus(s.Name);

                list.Add(new ServiceItemModel
                {
                    ServiceName = s.Name,
                    DisplayName = s.Display,
                    Description = s.Desc,
                    Status = status,
                    Impact = s.Impact,
                    Recommendation = s.Rec
                });
            }

            return list;
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
                proc?.WaitForExit(2000);

                string newStatus = QueryServiceStatus(serviceName);
                bool success = start ? newStatus == "RUNNING" : newStatus == "STOPPED";
                return (success, $"Service '{serviceName}' is now {newStatus}.");
            }
            catch (Exception ex)
            {
                return (false, $"Service action failed: {ex.Message}");
            }
        }
    }
}
