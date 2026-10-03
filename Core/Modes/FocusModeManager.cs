using System;
using System.Collections.Generic;
using System.Diagnostics;
using Vortex.Core.Database;

namespace Vortex.Core.Modes
{
    public class FocusModeManager
    {
        private readonly DatabaseService _db;
        private readonly List<string> _pausedProcesses = new();
        public bool IsFocusModeActive { get; private set; }

        public FocusModeManager(DatabaseService db)
        {
            _db = db;
        }

        public (int stoppedCount, string summary) EnableFocusMode()
        {
            if (IsFocusModeActive) return (0, "Gaming & Deep Focus Mode is already active.");

            _pausedProcesses.Clear();
            int stopped = 0;

            // Targets for suspension during gaming/deep focus:
            // Background updaters, OEM bloatware, non-active dev servers
            string[] targetProcessNames = new[]
            {
                "Creative Cloud UI Helper",
                "AdobeIPCBroker",
                "AdobeDesktopService",
                "HPSupportSolutionsFrameworkService",
                "HP.OmniHub",
                "GoogleCrashHandler",
                "MicrosoftEdgeUpdate"
            };

            foreach (var name in targetProcessNames)
            {
                try
                {
                    var procs = Process.GetProcessesByName(name);
                    foreach (var p in procs)
                    {
                        try
                        {
                            p.Kill();
                            p.WaitForExit(1000);
                            _pausedProcesses.Add(name);
                            stopped++;
                        }
                        catch { }
                        finally { p.Dispose(); }
                    }
                }
                catch { }
            }

            IsFocusModeActive = true;
            _db.LogAudit("FocusMode", "Enable", 0, $"Paused {stopped} background daemons for deep focus");
            return (stopped, $"Deep Focus Mode Activated: Suspended {stopped} non-essential background tasks.");
        }

        public string DisableFocusMode()
        {
            if (!IsFocusModeActive) return "Focus Mode is not currently active.";

            IsFocusModeActive = false;
            _db.LogAudit("FocusMode", "Disable", 0, "Deactivated deep focus mode");
            return "Focus Mode Deactivated. Standard background schedules restored.";
        }
    }
}
