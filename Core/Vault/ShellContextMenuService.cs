using System;
using System.IO;
using Microsoft.Win32;

namespace Vortex.Core.Vault
{
    /// <summary>
    /// Manages Windows Explorer right-click context menu integration for Vault and USB DLP.
    /// Uses user-level registry (HKCU) so it operates cleanly without requiring driver elevation.
    /// </summary>
    public static class ShellContextMenuService
    {
        private const string FolderShellPath = @"Software\Classes\Directory\shell\VortexVault";
        private const string DriveShellPath = @"Software\Classes\Drive\shell\VortexUsb";

        public static bool IsContextMenuRegistered()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(FolderShellPath, false);
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        public static (bool success, string message) RegisterContextMenu()
        {
            try
            {
                string exePath = Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Vortex.exe");
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vortex.ico");
                if (!File.Exists(icoPath)) icoPath = exePath;

                // 1. Directory Context Menu: "🛡️ Vortex: Lock / Protect Folder"
                using (var dirKey = Registry.CurrentUser.CreateSubKey(FolderShellPath, true))
                {
                    if (dirKey != null)
                    {
                        dirKey.SetValue("", "🛡️ Vortex: Protect & Lock Folder");
                        dirKey.SetValue("Icon", icoPath);
                        using var cmdKey = dirKey.CreateSubKey("command", true);
                        cmdKey?.SetValue("", $"\"{exePath}\" --vault-lock \"%1\"");
                    }
                }

                // 2. Drive Context Menu: "💾 Vortex: Authorize USB Write Access"
                using (var driveKey = Registry.CurrentUser.CreateSubKey(DriveShellPath, true))
                {
                    if (driveKey != null)
                    {
                        driveKey.SetValue("", "💾 Vortex: Authorize USB Write Access");
                        driveKey.SetValue("Icon", icoPath);
                        using var cmdKey = driveKey.CreateSubKey("command", true);
                        cmdKey?.SetValue("", $"\"{exePath}\" --usb-auth \"%1\"");
                    }
                }

                return (true, "Windows Explorer context menu registered successfully.");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to register context menu: {ex.Message}");
            }
        }

        public static (bool success, string message) UnregisterContextMenu()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(FolderShellPath, false);
                Registry.CurrentUser.DeleteSubKeyTree(DriveShellPath, false);
                return (true, "Context menu removed.");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to remove context menu: {ex.Message}");
            }
        }
    }
}
