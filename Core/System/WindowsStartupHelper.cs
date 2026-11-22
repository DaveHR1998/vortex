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

        public static bool IsRunOnStartupEnabled()
        {
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
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
                if (key == null) return;

                if (enable)
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

                    if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                    {
                        string command = startMinimized ? $"\"{exePath}\" --minimized" : $"\"{exePath}\"";
                        key.SetValue(AppName, command);
                    }
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
            catch
            {
                // Silently handle permission restrictions if any
            }
        }
    }
}
