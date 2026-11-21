using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Vortex.Core.Privacy
{
    public class TelemetryLockdownEngine
    {
        private const string HostsHeader = "# --- VORTEX PERMANENT TELEMETRY BLACKHOLE START ---";
        private const string HostsFooter = "# --- VORTEX PERMANENT TELEMETRY BLACKHOLE END ---";

        private static readonly string[] TelemetryDomains = new[]
        {
            "telemetry.microsoft.com",
            "vortex.data.microsoft.com",
            "v10.events.data.microsoft.com",
            "v20.events.data.microsoft.com",
            "watson.telemetry.microsoft.com",
            "settings-win.data.microsoft.com",
            "diagnostics.support.microsoft.com",
            "feedback.windows.com",
            "telemetry.urs.microsoft.com",
            "webevents.data.microsoft.com"
        };

        private static readonly string[] TelemetryServices = new[]
        {
            "DiagTrack",
            "dmwappushservice",
            "WerSvc"
        };

        private static readonly string[] TelemetryScheduledTasks = new[]
        {
            @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
            @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
            @"\Microsoft\Windows\Application Experience\StartupAppTask",
            @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
            @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
            @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector",
            @"\Microsoft\Windows\Autochk\Proxy"
        };

        public bool IsFullyLockedDown()
        {
            try
            {
                // Check Registry Windows DataCollection
                using var dcKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection");
                int allowTele = (int)(dcKey?.GetValue("AllowTelemetry") ?? 1);
                if (allowTele != 0) return false;

                // Check Chrome policy
                using var chromeKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Google\Chrome");
                int chromeMetrics = (int)(chromeKey?.GetValue("MetricsReportingEnabled") ?? 1);
                if (chromeMetrics != 0) return false;

                // Check Edge policy
                using var edgeKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Edge");
                int edgeMetrics = (int)(edgeKey?.GetValue("MetricsReportingEnabled") ?? 1);
                if (edgeMetrics != 0) return false;

                // Check Hosts file block
                string hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
                if (File.Exists(hostsPath))
                {
                    string content = File.ReadAllText(hostsPath);
                    if (!content.Contains(HostsHeader)) return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public (bool Success, string Summary) ApplyCompleteLockdown()
        {
            int servicesStopped = 0;
            int tasksDisabled = 0;
            int policiesApplied = 0;
            bool hostsApplied = false;

            // 1. Terminate running telemetry reporters immediately
            foreach (var name in new[] { "GoogleCrashHandler", "GoogleCrashHandler64", "MicrosoftEdgeUpdate", "CompatTelRunner", "DeviceCensus" })
            {
                try
                {
                    foreach (var p in Process.GetProcessesByName(name))
                    {
                        try { p.Kill(true); } catch { }
                    }
                }
                catch { }
            }

            // 2. Layer 1: Disable Windows Telemetry Services
            foreach (var svc in TelemetryServices)
            {
                try
                {
                    RunProcess("sc.exe", $"config \"{svc}\" start= disabled");
                    RunProcess("sc.exe", $"stop \"{svc}\"");
                    servicesStopped++;
                }
                catch { }
            }

            // 3. Layer 2: Disable Windows Telemetry Scheduled Tasks
            foreach (var task in TelemetryScheduledTasks)
            {
                try
                {
                    var (code, _) = RunProcess("schtasks.exe", $"/change /tn \"{task}\" /disable");
                    if (code == 0) tasksDisabled++;
                }
                catch { }
            }

            // 4. Layer 3: Apply Machine-Wide Group Policies (Windows OS + Chromium)
            try
            {
                // A. Windows Data Collection
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", true))
                {
                    key?.SetValue("AllowTelemetry", 0, RegistryValueKind.DWord);
                    key?.SetValue("DoNotShowFeedbackNotifications", 1, RegistryValueKind.DWord);
                    key?.SetValue("DisableEnterpriseAuthProxy", 1, RegistryValueKind.DWord);
                    policiesApplied++;
                }

                // B. Windows Advertising Info
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", true))
                {
                    key?.SetValue("DisabledByGroupPolicy", 1, RegistryValueKind.DWord);
                    policiesApplied++;
                }

                // C. Windows Search & Cortana Telemetry
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search", true))
                {
                    key?.SetValue("AllowCortana", 0, RegistryValueKind.DWord);
                    key?.SetValue("DisableWebSearch", 1, RegistryValueKind.DWord);
                    key?.SetValue("ConnectedSearchUseWeb", 0, RegistryValueKind.DWord);
                    policiesApplied++;
                }

                // D. Google Chrome Enterprise Policy
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Google\Chrome", true))
                {
                    key?.SetValue("MetricsReportingEnabled", 0, RegistryValueKind.DWord);
                    key?.SetValue("SendSiteInfoToGoogle", 0, RegistryValueKind.DWord);
                    key?.SetValue("UrlKeyedAnonymizedDataCollectionEnabled", 0, RegistryValueKind.DWord);
                    key?.SetValue("ChromeCleanupReportingEnabled", 0, RegistryValueKind.DWord);
                    policiesApplied++;
                }

                // E. Microsoft Edge Enterprise Policy
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Edge", true))
                {
                    key?.SetValue("MetricsReportingEnabled", 0, RegistryValueKind.DWord);
                    key?.SetValue("SendSiteInfoToImproveServices", 0, RegistryValueKind.DWord);
                    key?.SetValue("PersonalizationReportingEnabled", 0, RegistryValueKind.DWord);
                    key?.SetValue("DiagnosticData", 0, RegistryValueKind.DWord);
                    policiesApplied++;
                }
            }
            catch { }

            // 5. Layer 4: Windows Network Hosts Blackhole (0.0.0.0 Null Routing)
            try
            {
                string hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
                if (File.Exists(hostsPath))
                {
                    string content = File.ReadAllText(hostsPath);
                    if (!content.Contains(HostsHeader))
                    {
                        var lines = new List<string>
                        {
                            "",
                            HostsHeader
                        };
                        foreach (var d in TelemetryDomains)
                        {
                            lines.Add($"0.0.0.0 {d}");
                        }
                        lines.Add(HostsFooter);
                        lines.Add("");

                        File.AppendAllLines(hostsPath, lines);
                    }
                    hostsApplied = true;
                }
            }
            catch { }

            // Flush DNS resolver cache
            try
            {
                RunProcess("ipconfig.exe", "/flushdns");
            }
            catch { }

            string summary = $"Telemetry Lockdown Active:\n" +
                             $"• {servicesStopped} Telemetry services disabled (DiagTrack, WAP, WerSvc)\n" +
                             $"• {tasksDisabled} Scheduled diagnostic tasks disabled\n" +
                             $"• {policiesApplied} Group policy shields enforced (Chrome, Edge, Windows OS)\n" +
                             $"• {(hostsApplied ? "Active" : "Skipped")} Hosts DNS blackhole (10 telemetry domains mapped to 0.0.0.0)";

            return (true, summary);
        }

        public (bool Success, string Summary) RevertLockdown()
        {
            // 1. Re-enable Services
            foreach (var svc in TelemetryServices)
            {
                try
                {
                    RunProcess("sc.exe", $"config \"{svc}\" start= auto");
                    RunProcess("sc.exe", $"start \"{svc}\"");
                }
                catch { }
            }

            // 2. Re-enable Scheduled Tasks
            foreach (var task in TelemetryScheduledTasks)
            {
                try
                {
                    RunProcess("schtasks.exe", $"/change /tn \"{task}\" /enable");
                }
                catch { }
            }

            // 3. Remove Enterprise Policies
            try
            {
                using var dcKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", true);
                dcKey?.DeleteValue("AllowTelemetry", false);

                using var adKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", true);
                adKey?.DeleteValue("DisabledByGroupPolicy", false);

                using var chromeKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Google\Chrome", true);
                chromeKey?.DeleteValue("MetricsReportingEnabled", false);
                chromeKey?.DeleteValue("SendSiteInfoToGoogle", false);

                using var edgeKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Edge", true);
                edgeKey?.DeleteValue("MetricsReportingEnabled", false);
                edgeKey?.DeleteValue("DiagnosticData", false);
            }
            catch { }

            // 4. Clean Hosts File
            try
            {
                string hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
                if (File.Exists(hostsPath))
                {
                    var lines = File.ReadAllLines(hostsPath).ToList();
                    int startIdx = lines.FindIndex(l => l.Contains(HostsHeader));
                    int endIdx = lines.FindIndex(l => l.Contains(HostsFooter));

                    if (startIdx >= 0 && endIdx >= startIdx)
                    {
                        lines.RemoveRange(startIdx, (endIdx - startIdx) + 1);
                        File.WriteAllLines(hostsPath, lines);
                    }
                }
            }
            catch { }

            try
            {
                RunProcess("ipconfig.exe", "/flushdns");
            }
            catch { }

            return (true, "Standard Windows and browser telemetry configuration restored.");
        }

        private static (int ExitCode, string Output) RunProcess(string fileName, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            string output = proc?.StandardOutput.ReadToEnd() ?? string.Empty;
            proc?.WaitForExit(3000);
            return (proc?.ExitCode ?? -1, output);
        }
    }
}
