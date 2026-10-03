using System;
using System.Diagnostics;
using System.Management;

namespace Vortex.Core.SystemIntegration
{
    public class SystemRestoreService
    {
        public static (bool success, string message) CreateRestorePoint(string description)
        {
            try
            {
                // WMI invocation on root\default:SystemRestore
                using var restoreClass = new ManagementClass(@"root\default:SystemRestore");
                using var inParams = restoreClass.GetMethodParameters("CreateRestorePoint");

                inParams["Description"] = $"Vortex Pre-Clean: {description}";
                inParams["RestorePointType"] = 0; // APPLICATION_INSTALL / System change
                inParams["EventType"] = 100;       // BEGIN_SYSTEM_CHANGE

                using var outParams = restoreClass.InvokeMethod("CreateRestorePoint", inParams, null);
                uint returnValue = (uint)(outParams["ReturnValue"] ?? 1);

                if (returnValue == 0)
                {
                    return (true, "Windows System Restore point created successfully.");
                }
                else
                {
                    return (false, $"System Restore API returned code {returnValue}.");
                }
            }
            catch (Exception ex)
            {
                // Fallback to PowerShell Checkpoint-Computer
                try
                {
                    var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -Command \"Checkpoint-Computer -Description 'Vortex Pre-Clean: {description}' -RestorePointType 'MODIFY_SETTINGS'\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(4000);
                    return (true, "Windows System Restore point initialized via PowerShell checkpoint.");
                }
                catch
                {
                    return (false, $"Unable to create restore point: {ex.Message}");
                }
            }
        }
    }
}
