using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Vortex.Core.SystemIntegration
{
    public static class WindowsStartupHelper
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "Vortex";
        private const string TaskName = "VortexGuardianStartup";

        public static bool IsRunOnStartupEnabled()
        {
            // 1. Check Task Scheduler (highest reliability for elevated apps on boot)
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/query /tn \"{TaskName}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                if (p != null)
                {
                    p.WaitForExit(1500);
                    if (p.ExitCode == 0) return true;
                }
            }
            catch { }

            // 2. Fallback check standard CurrentUser Run key
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
                return key?.GetValue(AppName) != null;
            }
            catch
            {
                return false;
            }
        }

        public static void SetRunOnStartup(bool enable, bool startMinimized = false)
        {
            string exePath = Environment.ProcessPath ?? string.Empty;
            if (string.IsNullOrEmpty(exePath))
            {
                try
                {
                    exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                }
                catch { }
            }

            if (enable && (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)))
            {
                return;
            }

            string startupArgs = startMinimized ? "--minimized --startup" : "--startup";
            string fullCommand = $"\"{exePath}\" {startupArgs}";

            // 1. Configure Windows Task Scheduler with Highest Privileges
            // This bypasses Windows Explorer silently ignoring elevated apps in the Run key on logon
            try
            {
                if (enable)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = $"/create /tn \"{TaskName}\" /tr \"\\\"{exePath}\\\" {startupArgs}\" /sc onlogon /rl highest /f",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit(3000);
                }
                else
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = $"/delete /tn \"{TaskName}\" /f",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit(2000);
                }
            }
            catch { }

            // 2. Dual-configure Registry Run Key as secondary/fallback handler
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
                if (key != null)
                {
                    if (enable)
                    {
                        key.SetValue(AppName, fullCommand);
                    }
                    else
                    {
                        key.DeleteValue(AppName, false);
                    }
                }
            }
            catch { }
        }
    }
}
