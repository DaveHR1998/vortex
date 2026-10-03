using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Core.Database;

namespace Vortex.Core.ProcessManagement
{
    public class NetworkMonitor
    {
        private const int AF_INET = 2; // IPv4
        private const int TCP_TABLE_OWNER_PID_ALL = 5;
        private const int UDP_TABLE_OWNER_PID = 1;

        #region Native Win32 Interop
        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(
            IntPtr pTcpTable,
            ref int pdwSize,
            bool bOrder,
            int ulAf,
            int tableClass,
            uint reserved);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedUdpTable(
            IntPtr pUdpTable,
            ref int pdwSize,
            bool bOrder,
            int ulAf,
            int tableClass,
            uint reserved);

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPROW_OWNER_PID
        {
            public uint state;
            public uint localAddr;
            public byte localPort1;
            public byte localPort2;
            public byte localPort3;
            public byte localPort4;
            public uint remoteAddr;
            public byte remotePort1;
            public byte remotePort2;
            public byte remotePort3;
            public byte remotePort4;
            public uint owningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_UDPROW_OWNER_PID
        {
            public uint localAddr;
            public byte localPort1;
            public byte localPort2;
            public byte localPort3;
            public byte localPort4;
            public uint owningPid;
        }
        #endregion

        #region Models
        public class NetworkConnectionInfo
        {
            public int ProcessId { get; set; }
            public string ProcessName { get; set; } = string.Empty;
            public string ProcessPath { get; set; } = string.Empty;
            public string ProcessTitle { get; set; } = string.Empty;
            public string UserContext { get; set; } = string.Empty;
            public string IconGlyph { get; set; } = "💻";
            public string LocalAddress { get; set; } = string.Empty;
            public string RemoteAddress { get; set; } = string.Empty;
            public string RemoteDomain { get; set; } = string.Empty;
            public string Protocol { get; set; } = "TCP";
            public string ServiceName { get; set; } = string.Empty;
            public string State { get; set; } = string.Empty;
            public string Direction { get; set; } = "Outbound";
            public bool IsSuspicious { get; set; } = false;
            public bool IsAllowed { get; set; } = false;
            public string ThreatReason { get; set; } = string.Empty;
            public string ThreatSeverity { get; set; } = "NONE"; // NONE, ALLOWED, SUSPICIOUS, CRITICAL
            public bool IsVpnTunnel { get; set; } = false;
            public string DisplayRemote => string.IsNullOrEmpty(RemoteDomain) 
                ? RemoteAddress 
                : $"{RemoteAddress} ({RemoteDomain})";
        }

        public class BandwidthSnapshot
        {
            public double DownloadSpeedBytesPerSec { get; set; }
            public double UploadSpeedBytesPerSec { get; set; }
            public string DownloadSpeedFormatted => FormatSpeed(DownloadSpeedBytesPerSec);
            public string UploadSpeedFormatted => FormatSpeed(UploadSpeedBytesPerSec);
            public long TotalBytesReceived { get; set; }
            public long TotalBytesSent { get; set; }
            public string TotalReceivedFormatted => FormatBytes(TotalBytesReceived);
            public string TotalSentFormatted => FormatBytes(TotalBytesSent);
            public string ActiveInterfaceName { get; set; } = "Ethernet / Wi-Fi";
            public bool IsVpnActive { get; set; } = false;
            public string VpnInterfaceName { get; set; } = string.Empty;

            private static string FormatSpeed(double bytesPerSec)
            {
                if (bytesPerSec >= 1024 * 1024)
                    return $"{(bytesPerSec / (1024 * 1024)):F1} MB/s";
                if (bytesPerSec >= 1024)
                    return $"{(bytesPerSec / 1024):F0} KB/s";
                return $"{bytesPerSec:F0} B/s";
            }

            private static string FormatBytes(long bytes)
            {
                if (bytes >= 1024L * 1024 * 1024)
                    return $"{(bytes / (1024.0 * 1024 * 1024)):F2} GB";
                if (bytes >= 1024 * 1024)
                    return $"{(bytes / (1024.0 * 1024)):F1} MB";
                if (bytes >= 1024)
                    return $"{(bytes / 1024.0):F0} KB";
                return $"{bytes} B";
            }
        }
        #endregion

        #region Internal State & Caches
        private readonly ConcurrentDictionary<string, string> _dnsCache = new();
        private readonly ConcurrentDictionary<int, (string Name, string Path, string Title, string User, string Glyph)> _procMetaCache = new();
        private readonly HashSet<string> _notifiedThreatKeys = new();
        private readonly DatabaseService? _db;

        private long _lastBytesReceived = 0;
        private long _lastBytesSent = 0;
        private DateTime _lastBandwidthSampleTime = DateTime.MinValue;

        public event Action<NetworkConnectionInfo, string>? OnThreatDetected;

        private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "csrss", "lsass", "smss", "services", "wininit", "winlogon", "explorer", "System", "vortex"
        };
        #endregion

        #region Constructors
        public NetworkMonitor(DatabaseService? db = null)
        {
            _db = db;
        }
        #endregion

        #region Public Methods

        public Dictionary<int, int> GetSocketCountPerProcess()
        {
            var dict = new Dictionary<int, int>();
            try
            {
                int bufferSize = 0;
                GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
                if (bufferSize <= 0) return dict;

                int allocSize = bufferSize + 8192;
                IntPtr pTable = Marshal.AllocHGlobal(allocSize);
                try
                {
                    int reqSize = allocSize;
                    uint result = GetExtendedTcpTable(pTable, ref reqSize, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
                    if (result == 0)
                    {
                        int numEntries = Marshal.ReadInt32(pTable);
                        int rowSize = Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID));
                        int maxSafeEntries = Math.Max(0, (reqSize - 4) / rowSize);
                        int count = Math.Min(numEntries, maxSafeEntries);

                        IntPtr rowPtr = IntPtr.Add(pTable, 4);
                        for (int i = 0; i < count; i++)
                        {
                            var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                            int pid = (int)row.owningPid;
                            dict[pid] = dict.GetValueOrDefault(pid, 0) + 1;
                            rowPtr = IntPtr.Add(rowPtr, rowSize);
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pTable);
                }
            }
            catch { }

            return dict;
        }

        public List<NetworkConnectionInfo> GetAllConnections(bool includeUdp = true)
        {
            var list = new List<NetworkConnectionInfo>();
            RefreshProcessMetadataSnapshot();

            // Check if active VPN adapter is present to flag endpoints
            var (vpnActive, vpnName) = CheckVpnStatus();

            // 1. TCP Connections
            try
            {
                int bufferSize = 0;
                GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
                if (bufferSize > 0)
                {
                    int allocSize = bufferSize + 8192;
                    IntPtr pTable = Marshal.AllocHGlobal(allocSize);
                    try
                    {
                        int reqSize = allocSize;
                        uint result = GetExtendedTcpTable(pTable, ref reqSize, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
                        if (result == 0)
                        {
                            int numEntries = Marshal.ReadInt32(pTable);
                            int rowSize = Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID));
                            int maxSafeEntries = Math.Max(0, (reqSize - 4) / rowSize);
                            int count = Math.Min(numEntries, maxSafeEntries);

                            IntPtr rowPtr = IntPtr.Add(pTable, 4);
                            for (int i = 0; i < count; i++)
                            {
                                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                                int localPort = (row.localPort1 << 8) + row.localPort2;
                                int remotePort = (row.remotePort1 << 8) + row.remotePort2;

                                string localIp = new IPAddress(row.localAddr).ToString();
                                string remoteIp = new IPAddress(row.remoteAddr).ToString();

                                string stateStr = MapTcpState(row.state);
                                int pid = (int)row.owningPid;

                                var meta = GetProcessMeta(pid);
                                string service = GetServiceName(remotePort != 0 ? remotePort : localPort);

                                string direction = "Outbound";
                                if (stateStr == "LISTEN")
                                    direction = "📥 Inbound / Listening";
                                else if (localPort <= 1024 && remotePort > 1024)
                                    direction = "📥 Inbound Client";
                                else
                                    direction = "📤 Outbound";

                                string domain = ResolveDomainCached(remoteIp);

                                var conn = new NetworkConnectionInfo
                                {
                                    ProcessId = pid,
                                    ProcessName = meta.Name,
                                    ProcessPath = meta.Path,
                                    ProcessTitle = meta.Title,
                                    UserContext = meta.User,
                                    IconGlyph = meta.Glyph,
                                    LocalAddress = $"{localIp}:{localPort}",
                                    RemoteAddress = remotePort == 0 ? "-" : $"{remoteIp}:{remotePort}",
                                    RemoteDomain = domain,
                                    Protocol = "TCP",
                                    ServiceName = service,
                                    State = stateStr,
                                    Direction = direction,
                                    IsVpnTunnel = vpnActive && (localIp.StartsWith("10.") || localIp.StartsWith("100."))
                                };

                                EvaluateThreatHeuristics(conn, localPort, remotePort, remoteIp);
                                list.Add(conn);

                                rowPtr = IntPtr.Add(rowPtr, rowSize);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pTable);
                    }
                }
            }
            catch { }

            // 2. UDP Endpoints
            if (includeUdp)
            {
                try
                {
                    int udpBufferSize = 0;
                    GetExtendedUdpTable(IntPtr.Zero, ref udpBufferSize, true, AF_INET, UDP_TABLE_OWNER_PID, 0);
                    if (udpBufferSize > 0)
                    {
                        int allocSize = udpBufferSize + 8192;
                        IntPtr pTable = Marshal.AllocHGlobal(allocSize);
                        try
                        {
                            int reqSize = allocSize;
                            uint result = GetExtendedUdpTable(pTable, ref reqSize, true, AF_INET, UDP_TABLE_OWNER_PID, 0);
                            if (result == 0)
                            {
                                int numEntries = Marshal.ReadInt32(pTable);
                                int rowSize = Marshal.SizeOf(typeof(MIB_UDPROW_OWNER_PID));
                                int maxSafeEntries = Math.Max(0, (reqSize - 4) / rowSize);
                                int count = Math.Min(numEntries, maxSafeEntries);

                                IntPtr rowPtr = IntPtr.Add(pTable, 4);
                                for (int i = 0; i < count; i++)
                                {
                                    var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(rowPtr);
                                    int localPort = (row.localPort1 << 8) + row.localPort2;
                                    string localIp = new IPAddress(row.localAddr).ToString();
                                    int pid = (int)row.owningPid;

                                    var meta = GetProcessMeta(pid);
                                    string service = GetServiceName(localPort);

                                    string direction = localPort <= 1024 || localIp == "0.0.0.0" 
                                        ? "📥 UDP Listening" 
                                        : "📤 UDP Endpoint";

                                    var conn = new NetworkConnectionInfo
                                    {
                                        ProcessId = pid,
                                        ProcessName = meta.Name,
                                        ProcessPath = meta.Path,
                                        ProcessTitle = meta.Title,
                                        UserContext = meta.User,
                                        IconGlyph = meta.Glyph,
                                        LocalAddress = $"{localIp}:{localPort}",
                                        RemoteAddress = "*:* (UDP)",
                                        RemoteDomain = string.Empty,
                                        Protocol = "UDP",
                                        ServiceName = service,
                                        State = "ACTIVE",
                                        Direction = direction,
                                        IsVpnTunnel = vpnActive
                                    };

                                    EvaluateThreatHeuristics(conn, localPort, 0, string.Empty);
                                    list.Add(conn);

                                    rowPtr = IntPtr.Add(rowPtr, rowSize);
                                }
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(pTable);
                        }
                    }
                }
                catch { }
            }

            // Detect process connection spikes (>35 outbound sockets created)
            var connectionSpikes = list
                .Where(c => c.Direction.Contains("Outbound") && c.ProcessId > 4)
                .GroupBy(c => c.ProcessId)
                .Where(g => g.Count() >= 40);

            foreach (var group in connectionSpikes)
            {
                var sample = group.First();
                if (!sample.IsSuspicious)
                {
                    sample.IsSuspicious = true;
                    sample.ThreatSeverity = "SUSPICIOUS";
                    sample.ThreatReason = $"Connection Spike: Process has {group.Count()} concurrent outbound connections.";
                    TriggerThreatNotification(sample, sample.ThreatReason);
                }
            }

            return list;
        }

        public BandwidthSnapshot GetBandwidthSnapshot()
        {
            var snapshot = new BandwidthSnapshot();
            long currentBytesRecv = 0;
            long currentBytesSent = 0;
            string bestInterface = "Network";

            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var nic in interfaces)
                {
                    if (nic.OperationalStatus != OperationalStatus.Up ||
                        nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    var stats = nic.GetIPv4Statistics();
                    currentBytesRecv += stats.BytesReceived;
                    currentBytesSent += stats.BytesSent;

                    if (stats.BytesReceived > 0 && bestInterface == "Network")
                    {
                        bestInterface = nic.Name;
                    }
                }

                snapshot.TotalBytesReceived = currentBytesRecv;
                snapshot.TotalBytesSent = currentBytesSent;
                snapshot.ActiveInterfaceName = bestInterface;

                var (vpnActive, vpnName) = CheckVpnStatus();
                snapshot.IsVpnActive = vpnActive;
                snapshot.VpnInterfaceName = vpnName;

                DateTime now = DateTime.UtcNow;
                if (_lastBandwidthSampleTime != DateTime.MinValue && now > _lastBandwidthSampleTime)
                {
                    double elapsedSec = (now - _lastBandwidthSampleTime).TotalSeconds;
                    if (elapsedSec > 0.1 && currentBytesRecv >= _lastBytesReceived && currentBytesSent >= _lastBytesSent)
                    {
                        snapshot.DownloadSpeedBytesPerSec = (currentBytesRecv - _lastBytesReceived) / elapsedSec;
                        snapshot.UploadSpeedBytesPerSec = (currentBytesSent - _lastBytesSent) / elapsedSec;
                    }
                }

                _lastBytesReceived = currentBytesRecv;
                _lastBytesSent = currentBytesSent;
                _lastBandwidthSampleTime = now;
            }
            catch { }

            return snapshot;
        }

        public (bool IsActive, string InterfaceName) CheckVpnStatus()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var nic in interfaces)
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;

                    string desc = (nic.Description + " " + nic.Name).ToLowerInvariant();
                    if (desc.Contains("wintun") || desc.Contains("wireguard") || desc.Contains("tap-windows") ||
                        desc.Contains("openvpn") || desc.Contains("nordlynx") || desc.Contains("tailscale") ||
                        desc.Contains("warp") || desc.Contains("proton") || desc.Contains("vpn") ||
                        nic.NetworkInterfaceType == NetworkInterfaceType.Ppp)
                    {
                        return (true, nic.Name);
                    }
                }
            }
            catch { }

            return (false, string.Empty);
        }

        public (bool Success, string Message) KillProcess(int pid)
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                string name = proc.ProcessName;

                if (ProtectedProcesses.Contains(name))
                {
                    return (false, $"Process '{name}' (PID {pid}) is a protected Windows system component and cannot be killed.");
                }

                proc.Kill(true);
                return (true, $"Process '{name}' (PID {pid}) was terminated successfully.");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to terminate PID {pid}: {ex.Message}");
            }
        }

        public bool OpenProcessLocation(int pid)
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                string? path = null;
                try { path = proc.MainModule?.FileName; } catch { }

                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                    return true;
                }
            }
            catch { }
            return false;
        }

        #endregion

        #region Threat Heuristics & Intelligence

        public bool IsAppAllowed(string procName, string? exePath = null)
        {
            if (string.IsNullOrEmpty(procName)) return false;
            string clean = Path.GetFileNameWithoutExtension(procName);
            if (ProtectedProcesses.Contains(clean) || ProtectedProcesses.Contains(clean + ".exe"))
                return true;

            if (_db != null)
            {
                return _db.IsAppAllowed(procName, exePath);
            }

            return false;
        }

        private void EvaluateThreatHeuristics(NetworkConnectionInfo conn, int localPort, int remotePort, string remoteIp)
        {
            string name = conn.ProcessName.ToLowerInvariant();
            string path = conn.ProcessPath.ToLowerInvariant();

            // Check if process is explicitly Allowed / Whitelisted
            if (IsAppAllowed(conn.ProcessName, conn.ProcessPath))
            {
                conn.IsSuspicious = false;
                conn.IsAllowed = true;
                conn.ThreatSeverity = "ALLOWED";
                conn.ThreatReason = "Authorized Application";
                return;
            }

            // Rule 1: Process Masquerading (Spoofed System Executable)
            if ((name == "svchost" || name == "lsass" || name == "csrss" || name == "services" || name == "smss") &&
                !string.IsNullOrEmpty(path) &&
                !path.StartsWith(@"c:\windows\system32\", StringComparison.OrdinalIgnoreCase))
            {
                conn.IsSuspicious = true;
                conn.ThreatSeverity = "CRITICAL";
                conn.ThreatReason = $"CRITICAL SPOOF: '{conn.ProcessName}' is running outside System32 from '{conn.ProcessPath}'!";
                TriggerThreatNotification(conn, conn.ThreatReason);
                return;
            }

            // Rule 2: Rogue Inbound Listening Port from AppData / Temp / Downloads
            if (conn.State == "LISTEN" &&
                (path.Contains(@"\appdata\") || path.Contains(@"\temp\") || path.Contains(@"\downloads\")))
            {
                conn.IsSuspicious = true;
                conn.ThreatSeverity = "CRITICAL";
                conn.ThreatReason = $"ROGUE SERVER: Process in Temp/AppData opened listening port {localPort} for incoming connections.";
                TriggerThreatNotification(conn, conn.ThreatReason);
                return;
            }

            // Rule 3: Outbound connection from AppData\Local\Temp or Downloads to public IP
            if (conn.Direction.Contains("Outbound") &&
                (path.Contains(@"\temp\") || path.Contains(@"\downloads\")) &&
                !IsPrivateIp(remoteIp))
            {
                conn.IsSuspicious = true;
                conn.ThreatSeverity = "SUSPICIOUS";
                conn.ThreatReason = $"Untrusted Outbound: Process in Temp/Downloads is connecting to foreign IP {remoteIp}:{remotePort}.";
                TriggerThreatNotification(conn, conn.ThreatReason);
                return;
            }

            // Rule 4: Inbound connection to dangerous ports (SMB 445 / RDP 3389)
            if ((localPort == 445 || localPort == 3389) && conn.State == "ESTABLISHED" && !IsPrivateIp(remoteIp))
            {
                conn.IsSuspicious = true;
                conn.ThreatSeverity = "CRITICAL";
                conn.ThreatReason = $"DANGEROUS PORT EXPOSURE: Inbound connection established on administrative port {localPort} from {remoteIp}!";
                TriggerThreatNotification(conn, conn.ThreatReason);
                return;
            }
        }

        private void TriggerThreatNotification(NetworkConnectionInfo conn, string reason)
        {
            // Do not notify if app is allowed or if notifications are muted
            if (conn.IsAllowed) return;
            if (_db != null && _db.IsNotificationMuted(conn.ProcessName)) return;

            string key = $"{conn.ProcessId}_{conn.ThreatSeverity}_{reason}";
            lock (_notifiedThreatKeys)
            {
                if (_notifiedThreatKeys.Contains(key)) return;
                _notifiedThreatKeys.Add(key);
            }

            try
            {
                OnThreatDetected?.Invoke(conn, reason);
            }
            catch { }
        }

        private static bool IsPrivateIp(string ipStr)
        {
            if (string.IsNullOrEmpty(ipStr) || ipStr == "-" || ipStr.StartsWith("127.") || ipStr == "0.0.0.0" || ipStr == "::1")
                return true;

            if (IPAddress.TryParse(ipStr, out var ip))
            {
                byte[] bytes = ip.GetAddressBytes();
                if (bytes.Length == 4)
                {
                    // 10.0.0.0/8
                    if (bytes[0] == 10) return true;
                    // 172.16.0.0/12
                    if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                    // 192.168.0.0/16
                    if (bytes[0] == 192 && bytes[1] == 168) return true;
                    // 169.254.0.0/16 (link local)
                    if (bytes[0] == 169 && bytes[1] == 254) return true;
                }
            }
            return false;
        }

        #endregion

        #region Reverse DNS & Process Attribution Helpers

        private string ResolveDomainCached(string ipStr)
        {
            if (string.IsNullOrEmpty(ipStr) || ipStr == "-" || ipStr == "0.0.0.0")
                return string.Empty;

            if (IsPrivateIp(ipStr))
                return "Local / LAN";

            if (_dnsCache.TryGetValue(ipStr, out string? cached))
                return cached;

            // Trigger non-blocking asynchronous reverse DNS lookup
            _dnsCache[ipStr] = string.Empty; // Placeholder to prevent duplicate queries
            Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(1200);
                    var entry = await Dns.GetHostEntryAsync(ipStr);
                    if (!string.IsNullOrEmpty(entry?.HostName))
                    {
                        string host = entry.HostName;
                        // Shorten if too long
                        _dnsCache[ipStr] = host;
                    }
                }
                catch
                {
                    _dnsCache[ipStr] = string.Empty;
                }
            });

            return string.Empty;
        }

        private void RefreshProcessMetadataSnapshot()
        {
            try
            {
                var procs = Process.GetProcesses();
                foreach (var p in procs)
                {
                    try
                    {
                        int id = p.Id;
                        if (id == 0) continue;

                        if (!_procMetaCache.ContainsKey(id))
                        {
                            string name = p.ProcessName;
                            string path = string.Empty;
                            string title = string.Empty;
                            string user = string.Empty;

                            try { path = p.MainModule?.FileName ?? string.Empty; } catch { }
                            try { title = p.MainWindowTitle; } catch { }

                            string glyph = DetermineIconGlyph(name, path);
                            _procMetaCache[id] = (name, path, title, user, glyph);
                        }
                    }
                    catch { }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }
            catch { }
        }

        private (string Name, string Path, string Title, string User, string Glyph) GetProcessMeta(int pid)
        {
            if (pid == 0) return ("System Idle", string.Empty, string.Empty, "SYSTEM", "💤");
            if (pid == 4) return ("System", @"C:\Windows\System32\ntoskrnl.exe", string.Empty, "SYSTEM", "⚙️");

            if (_procMetaCache.TryGetValue(pid, out var meta))
                return meta;

            try
            {
                using var p = Process.GetProcessById(pid);
                string name = p.ProcessName;
                string path = string.Empty;
                string title = string.Empty;
                try { path = p.MainModule?.FileName ?? string.Empty; } catch { }
                try { title = p.MainWindowTitle; } catch { }
                string glyph = DetermineIconGlyph(name, path);
                var result = (name, path, title, string.Empty, glyph);
                _procMetaCache[pid] = result;
                return result;
            }
            catch
            {
                return ($"PID {pid}", string.Empty, string.Empty, string.Empty, "💻");
            }
        }

        private static string DetermineIconGlyph(string procName, string path)
        {
            string name = procName.ToLowerInvariant();
            if (name.Contains("chrome") || name.Contains("edge") || name.Contains("firefox") || name.Contains("brave") || name.Contains("opera"))
                return "🌐";
            if (name.Contains("discord") || name.Contains("slack") || name.Contains("teams") || name.Contains("telegram"))
                return "💬";
            if (name.Contains("steam") || name.Contains("game") || name.Contains("epic") || name.Contains("riot"))
                return "🎮";
            if (name.Contains("spotify") || name.Contains("vlc") || name.Contains("media"))
                return "🎵";
            if (name.Contains("code") || name.Contains("devenv") || name.Contains("git") || name.Contains("node"))
                return "⚡";
            if (name.Contains("svchost") || name.Contains("services") || name.Contains("system"))
                return "⚙️";
            if (name.Contains("wireguard") || name.Contains("vpn") || name.Contains("nord") || name.Contains("tailscale"))
                return "🛡️";
            if (path.Contains(@"\temp\") || path.Contains(@"\downloads\"))
                return "⚠️";
            return "💻";
        }

        private static string GetServiceName(int port) => port switch
        {
            80 => "HTTP",
            443 => "HTTPS",
            53 => "DNS",
            22 => "SSH",
            21 => "FTP",
            25 => "SMTP",
            110 => "POP3",
            993 => "IMAPS",
            995 => "POP3S",
            3389 => "RDP (Remote Desktop)",
            445 => "SMB (File Sharing)",
            135 => "RPC",
            139 => "NetBIOS",
            8080 => "HTTP-Proxy",
            8443 => "HTTPS-Alt",
            51820 => "WireGuard VPN",
            1194 => "OpenVPN",
            27015 => "Steam Game Server",
            3074 => "Xbox Live",
            _ => string.Empty
        };

        private static string MapTcpState(uint state) => state switch
        {
            1 => "CLOSED",
            2 => "LISTEN",
            3 => "SYN_SENT",
            4 => "SYN_RCVD",
            5 => "ESTABLISHED",
            6 => "FIN_WAIT1",
            7 => "FIN_WAIT2",
            8 => "CLOSE_WAIT",
            9 => "CLOSING",
            10 => "LAST_ACK",
            11 => "TIME_WAIT",
            12 => "DELETE_TCB",
            _ => "UNKNOWN"
        };

        #endregion
    }
}

