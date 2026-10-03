using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Vortex.Core.Database;

namespace Vortex.Core.ProcessManagement
{
    public class ProcessManager
    {
        private readonly DatabaseService _db;
        private readonly NetworkMonitor _netMonitor;

        public ProcessManager(DatabaseService db, NetworkMonitor netMonitor)
        {
            _db = db;
            _netMonitor = netMonitor;
        }

        public List<ProcessInfoModel> GetRunningProcesses()
        {
            var result = new List<ProcessInfoModel>();
            var socketCounts = _netMonitor.GetSocketCountPerProcess();

            var processes = Process.GetProcesses();
            foreach (var proc in processes)
            {
                try
                {
                    if (proc.Id == 0) continue; // Skip idle

                    string procName = proc.ProcessName;
                    long mem = proc.WorkingSet64;
                    int pid = proc.Id;
                    int sockets = socketCounts.GetValueOrDefault(pid, 0);

                    var rule = _db.GetProcessRule(procName);
                    if (rule != null)
                    {
                        result.Add(new ProcessInfoModel
                        {
                            Id = pid,
                            Name = procName,
                            DisplayName = rule.DisplayName,
                            Publisher = rule.Publisher,
                            Description = rule.Description,
                            MemoryBytes = mem,
                            SafetyLevel = rule.SafetyLevel,
                            Recommendation = rule.Recommendation,
                            ActiveSockets = sockets
                        });
                    }
                    else
                    {
                        // Heuristic evaluation for uncatalogued processes
                        string filePath = string.Empty;
                        string company = string.Empty;
                        string fileDesc = string.Empty;

                        try
                        {
                            filePath = proc.MainModule?.FileName ?? string.Empty;
                            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                            {
                                var version = FileVersionInfo.GetVersionInfo(filePath);
                                company = version.CompanyName ?? string.Empty;
                                fileDesc = version.FileDescription ?? string.Empty;
                            }
                        }
                        catch
                        {
                            // In case of elevation boundaries
                        }

                        var safety = ProcessSafetyLevel.UserApplication;
                        string rec = "User application. Close if not in use.";

                        if (filePath.Contains("Windows\\System32", StringComparison.OrdinalIgnoreCase) ||
                            company.Contains("Microsoft Corporation", StringComparison.OrdinalIgnoreCase))
                        {
                            safety = ProcessSafetyLevel.SystemCritical;
                            rec = "Windows operating system component. Do not terminate.";
                        }
                        else if (procName.Contains("update", StringComparison.OrdinalIgnoreCase) ||
                                 procName.Contains("helper", StringComparison.OrdinalIgnoreCase) ||
                                 procName.Contains("crash", StringComparison.OrdinalIgnoreCase) ||
                                 procName.Contains("telemetry", StringComparison.OrdinalIgnoreCase))
                        {
                            safety = ProcessSafetyLevel.SafeToTerminate;
                            rec = "Background updater or telemetry helper. Safe to terminate.";
                        }

                        result.Add(new ProcessInfoModel
                        {
                            Id = pid,
                            Name = procName,
                            DisplayName = string.IsNullOrEmpty(fileDesc) ? procName : fileDesc,
                            Publisher = string.IsNullOrEmpty(company) ? "Unknown Publisher" : company,
                            Description = string.IsNullOrEmpty(fileDesc) ? $"Process: {procName}.exe" : fileDesc,
                            MemoryBytes = mem,
                            SafetyLevel = safety,
                            Recommendation = rec,
                            ActiveSockets = sockets
                        });
                    }
                }
                catch
                {
                    // Access denied or process exited
                }
                finally
                {
                    proc.Dispose();
                }
            }

            return result.OrderByDescending(p => p.MemoryBytes).ToList();
        }

        public (bool success, string message) TerminateProcess(int processId)
        {
            try
            {
                using var proc = Process.GetProcessById(processId);
                string name = proc.ProcessName;
                long mem = proc.WorkingSet64;

                var rule = _db.GetProcessRule(name);
                if (rule != null && rule.SafetyLevel == ProcessSafetyLevel.SystemCritical)
                {
                    return (false, $"Process '{name}' is protected by Windows core safety policy.");
                }

                proc.Kill(true);
                proc.WaitForExit(1500);

                _db.LogAudit("ProcessKill", name, mem, $"Terminated process PID {processId}");
                return (true, $"Successfully terminated '{name}' (Freed {mem / (1024.0 * 1024.0):F1} MB)");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to terminate process: {ex.Message}");
            }
        }

        public (int terminatedCount, long totalFreedBytes, List<string> messages) TerminateMultipleProcesses(IEnumerable<int> processIds)
        {
            int terminated = 0;
            long freed = 0;
            var msgs = new List<string>();

            foreach (var pid in processIds)
            {
                try
                {
                    using var proc = Process.GetProcessById(pid);
                    string name = proc.ProcessName;
                    long mem = proc.WorkingSet64;

                    var rule = _db.GetProcessRule(name);
                    if (rule != null && rule.SafetyLevel == ProcessSafetyLevel.SystemCritical)
                    {
                        msgs.Add($"Skipped '{name}' (Windows System Critical).");
                        continue;
                    }

                    proc.Kill(true);
                    proc.WaitForExit(1000);
                    terminated++;
                    freed += mem;
                    _db.LogAudit("ProcessKill", name, mem, $"Terminated process PID {pid}");
                    msgs.Add($"Terminated '{name}' ({mem / (1024.0 * 1024.0):F1} MB)");
                }
                catch (Exception ex)
                {
                    msgs.Add($"PID {pid}: {ex.Message}");
                }
            }

            return (terminated, freed, msgs);
        }
    }
}
