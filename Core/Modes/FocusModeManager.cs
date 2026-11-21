using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Vortex.Core.Database;

namespace Vortex.Core.Modes
{
    public class FocusProcessCandidate
    {
        public int Pid { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public long MemoryBytes { get; set; }
        public string MemoryDisplay => $"{MemoryBytes / (1024.0 * 1024.0):F1} MB";
        public bool IsSelected { get; set; } = true;
    }

    /// <summary>
    /// Deep Focus Mode Manager:
    /// 1. Freezes non-essential background processes via native Win32 NtSuspendProcess (and wakes them on exit via NtResumeProcess).
    /// 2. Enforces Windows Do Not Disturb / Focus Assist notification suppression.
    /// 3. Switches power plan to High Performance and restores the previous scheme on deactivation.
    /// 4. Supports Pomodoro / custom countdown timers.
    /// </summary>
    public class FocusModeManager
    {
        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSuspendProcess(IntPtr processHandle);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtResumeProcess(IntPtr processHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_SUSPEND_RESUME = 0x0800;

        private readonly DatabaseService _db;
        private readonly List<int> _suspendedPids = new();
        private string? _previousPowerPlanGuid;
        private int? _previousDndValue;

        public bool IsFocusModeActive { get; private set; }
        public DateTime? SessionStartTime { get; private set; }
        public int SessionDurationMinutes { get; private set; }
        public int SuspendedProcessCount => _suspendedPids.Count;
        public long QuarantinedMemoryBytes { get; private set; }

        public FocusModeManager(DatabaseService db)
        {
            _db = db;
        }

        private static readonly Dictionary<string, string> KnownBackgroundTargets = new(StringComparer.OrdinalIgnoreCase)
        {
            // Communication & Chat
            { "Discord", "Chat & Community" },
            { "DiscordCanary", "Chat & Community" },
            { "Slack", "Team Communication" },
            { "Teams", "Team Communication" },
            { "ms-teams", "Team Communication" },
            { "WhatsApp", "Messaging" },
            { "Telegram", "Messaging" },
            { "Spotify", "Media Streaming" },
            
            // Launchers & Stores
            { "Steam", "Store Launcher" },
            { "steamwebhelper", "Store Helper" },
            { "EpicGamesLauncher", "Store Launcher" },
            { "Battle.net", "Store Launcher" },
            { "Origin", "Store Launcher" },
            { "UbisoftConnect", "Store Launcher" },
            { "RiotClientServices", "Store Launcher" },
            { "GalaxyClient", "Store Launcher" },

            // Cloud Sync Clients
            { "OneDrive", "Cloud Storage Sync" },
            { "Dropbox", "Cloud Storage Sync" },
            { "GoogleDriveFS", "Cloud Storage Sync" },

            // Background Daemons & Updaters
            { "Creative Cloud UI Helper", "Creative Cloud Helper" },
            { "AdobeIPCBroker", "Adobe IPC Service" },
            { "AdobeDesktopService", "Adobe Desktop Helper" },
            { "HPSupportSolutionsFrameworkService", "OEM Diagnostics" },
            { "HP.OmniHub", "OEM Diagnostic Hub" },
            { "GoogleCrashHandler", "Browser Crash Handler" },
            { "GoogleCrashHandler64", "Browser Crash Handler" },
            { "MicrosoftEdgeUpdate", "Edge Update Worker" }
        };

        public List<FocusProcessCandidate> GetDiscoverableBackgroundProcesses()
        {
            var results = new List<FocusProcessCandidate>();
            var currentPid = Process.GetCurrentProcess().Id;

            foreach (var kvp in KnownBackgroundTargets)
            {
                try
                {
                    var procs = Process.GetProcessesByName(kvp.Key);
                    foreach (var p in procs)
                    {
                        try
                        {
                            if (p.Id == currentPid || p.HasExited) continue;
                            long mem = p.WorkingSet64;
                            results.Add(new FocusProcessCandidate
                            {
                                Pid = p.Id,
                                Name = p.ProcessName,
                                Category = kvp.Value,
                                MemoryBytes = mem,
                                IsSelected = true
                            });
                        }
                        catch { }
                        finally { p.Dispose(); }
                    }
                }
                catch { }
            }

            return results.OrderByDescending(r => r.MemoryBytes).ToList();
        }

        public (int suspendedCount, long quarantinedBytes, string summary) EnableFocusMode(
            IEnumerable<int>? targetPids = null, 
            int durationMinutes = 0,
            bool enableDnd = true, 
            bool setHighPerfPower = true)
        {
            if (IsFocusModeActive) return (0, 0, "Deep Focus Mode is already active.");

            _suspendedPids.Clear();
            long totalQuarantined = 0;
            int suspended = 0;

            List<int> pidsToSuspend;
            if (targetPids != null && targetPids.Any())
            {
                pidsToSuspend = targetPids.ToList();
            }
            else
            {
                pidsToSuspend = GetDiscoverableBackgroundProcesses().Select(p => p.Pid).ToList();
            }

            // 1. Suspend target processes via Win32 NtSuspendProcess
            foreach (int pid in pidsToSuspend)
            {
                try
                {
                    using var proc = Process.GetProcessById(pid);
                    if (proc.HasExited) continue;

                    long mem = proc.WorkingSet64;
                    IntPtr hProc = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
                    if (hProc != IntPtr.Zero)
                    {
                        int status = NtSuspendProcess(hProc);
                        CloseHandle(hProc);
                        if (status >= 0)
                        {
                            _suspendedPids.Add(pid);
                            totalQuarantined += mem;
                            suspended++;
                        }
                    }
                }
                catch { }
            }

            // 2. Enable Windows Do Not Disturb
            if (enableDnd)
            {
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings");
                    if (key != null)
                    {
                        var current = key.GetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED");
                        if (current != null) _previousDndValue = Convert.ToInt32(current);
                        key.SetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED", 0, RegistryValueKind.DWord);
                    }
                }
                catch { }
            }

            // 3. Set High Performance Power Plan
            if (setHighPerfPower)
            {
                try
                {
                    string schemeOutput = RunCmd("powercfg", "/getactivescheme");
                    var match = Regex.Match(schemeOutput, @"GUID:\s*([a-fA-F0-9\-]+)");
                    if (match.Success)
                    {
                        _previousPowerPlanGuid = match.Groups[1].Value;
                    }
                    RunCmd("powercfg", "/setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
                }
                catch { }
            }

            IsFocusModeActive = true;
            SessionStartTime = DateTime.UtcNow;
            SessionDurationMinutes = durationMinutes;
            QuarantinedMemoryBytes = totalQuarantined;

            _db.LogAudit("FocusMode", "Enable", totalQuarantined, $"Suspended {suspended} processes for deep focus (duration: {(durationMinutes > 0 ? $"{durationMinutes}m" : "manual")})");
            return (suspended, totalQuarantined, $"Deep Focus Mode Active: Suspended {suspended} background processes ({totalQuarantined / (1024.0 * 1024.0):F1} MB RAM quarantined).");
        }

        public (int resumedCount, string summary) DisableFocusMode()
        {
            if (!IsFocusModeActive) return (0, "Deep Focus Mode is not currently active.");

            int resumed = 0;

            // 1. Resume all suspended processes via Win32 NtResumeProcess
            foreach (int pid in _suspendedPids)
            {
                try
                {
                    IntPtr hProc = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
                    if (hProc != IntPtr.Zero)
                    {
                        int status = NtResumeProcess(hProc);
                        CloseHandle(hProc);
                        if (status >= 0) resumed++;
                    }
                }
                catch { }
            }
            _suspendedPids.Clear();

            // 2. Restore Windows Do Not Disturb
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings", writable: true);
                if (key != null)
                {
                    int restoreVal = _previousDndValue ?? 1;
                    key.SetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED", restoreVal, RegistryValueKind.DWord);
                }
            }
            catch { }

            // 3. Restore Previous Power Plan
            if (!string.IsNullOrEmpty(_previousPowerPlanGuid))
            {
                try
                {
                    RunCmd("powercfg", $"/setactive {_previousPowerPlanGuid}");
                    _previousPowerPlanGuid = null;
                }
                catch { }
            }

            IsFocusModeActive = false;
            SessionStartTime = null;
            SessionDurationMinutes = 0;
            QuarantinedMemoryBytes = 0;

            _db.LogAudit("FocusMode", "Disable", 0, $"Resumed {resumed} background processes; restored DND and power plan.");
            return (resumed, $"Deep Focus Mode Deactivated: Resumed {resumed} processes. Power plan & notifications restored.");
        }

        private static string RunCmd(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null) return string.Empty;
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                return output;
            }
            catch { return string.Empty; }
        }
    }
}
