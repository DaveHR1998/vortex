using System;
using System.Runtime.InteropServices;

namespace Vortex.Core.SystemIntegration
{
    public class PowerAndIdleManager
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus; // 0 = Offline (Battery), 1 = Online (AC), 255 = Unknown
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

        public TimeSpan GetIdleTime()
        {
            var lii = new LASTINPUTINFO();
            lii.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));

            if (GetLastInputInfo(ref lii))
            {
                uint currentTick = (uint)Environment.TickCount;
                uint idleTicks = currentTick - lii.dwTime;
                return TimeSpan.FromMilliseconds(idleTicks);
            }

            return TimeSpan.Zero;
        }

        public (bool isOnBattery, int batteryPercentage) GetPowerStatus()
        {
            if (GetSystemPowerStatus(out var status))
            {
                bool onBattery = status.ACLineStatus == 0;
                int percent = status.BatteryLifePercent;
                return (onBattery, percent > 100 ? 100 : percent);
            }
            return (false, 100);
        }
    }
}
