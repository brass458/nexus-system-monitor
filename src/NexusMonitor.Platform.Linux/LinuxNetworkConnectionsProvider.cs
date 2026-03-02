using System.Net;
using System.Reactive.Linq;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Helpers;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Platform.Linux;

public sealed class LinuxNetworkConnectionsProvider : INetworkConnectionsProvider
{
    private readonly AdapterThroughputTracker _adapterTracker = new();

    // inode→(pid, name) cache; rebuilt every ~2 seconds
    private Dictionary<long, (int pid, string name)> _inodeMap = new();
    private DateTime _inodeMapTime = DateTime.MinValue;

    private IObservable<IReadOnlyList<NetworkConnection>>? _shared;
    private readonly object _sharedLock = new();

    public bool SupportsPerConnectionThroughput => false;

    public IObservable<IReadOnlyList<NetworkConnection>> GetConnectionStream(TimeSpan interval)
    {
        lock (_sharedLock)
        {
            if (_shared is null)
            {
                _shared = Observable.Timer(TimeSpan.Zero, interval)
                                    .Select(_ => (IReadOnlyList<NetworkConnection>)GetConnections())
                                    .Publish()
                                    .RefCount();
            }
            return _shared;
        }
    }

    public Task<IReadOnlyList<NetworkConnection>> GetConnectionsAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<NetworkConnection>>(GetConnections, ct);

    public IObservable<AdapterThroughput> GetAdapterThroughputStream(TimeSpan interval) =>
        Observable.Timer(TimeSpan.Zero, interval)
                  .Select(_ => _adapterTracker.Sample());

    private IReadOnlyList<NetworkConnection> GetConnections()
    {
        RefreshInodeMapIfStale();

        var result = new List<NetworkConnection>();
        ParseProcNetFile("/proc/net/tcp",  ConnectionProtocol.Tcp4, false, result);
        ParseProcNetFile("/proc/net/tcp6", ConnectionProtocol.Tcp6, false, result);
        ParseProcNetFile("/proc/net/udp",  ConnectionProtocol.Udp4, true,  result);
        ParseProcNetFile("/proc/net/udp6", ConnectionProtocol.Udp6, true,  result);
        return result;
    }

    // ── Inode → PID map ────────────────────────────────────────────────────────
    private void RefreshInodeMapIfStale()
    {
        var now = DateTime.UtcNow;
        if ((now - _inodeMapTime).TotalSeconds < 2.0) return;

        _inodeMap     = BuildInodeMap();
        _inodeMapTime = now;
    }

    private static Dictionary<long, (int pid, string name)> BuildInodeMap()
    {
        var map = new Dictionary<long, (int, string)>();
        string[] pidDirs;
        try { pidDirs = Directory.GetDirectories("/proc"); }
        catch { return map; }

        foreach (var dir in pidDirs)
        {
            var dirName = Path.GetFileName(dir);
            if (!int.TryParse(dirName, out var pid)) continue;

            var fdDir = Path.Combine(dir, "fd");
            if (!Directory.Exists(fdDir)) continue;

            // Read process name from /proc/[pid]/comm
            string procName = string.Empty;
            try
            {
                var commPath = Path.Combine(dir, "comm");
                if (File.Exists(commPath))
                    procName = File.ReadAllText(commPath).Trim();
            }
            catch { }

            // Scan fd symlinks for socket:[inode]
            try
            {
                foreach (var fd in Directory.GetFiles(fdDir))
                {
                    try
                    {
                        var target = new FileInfo(fd).LinkTarget;
                        if (target == null) continue;
                        // Target looks like "socket:[12345678]"
                        if (!target.StartsWith("socket:[", StringComparison.Ordinal)) continue;
                        var inodeStr = target[8..^1]; // strip "socket:[" and "]"
                        if (long.TryParse(inodeStr, out var inode) && !map.ContainsKey(inode))
                            map[inode] = (pid, procName);
                    }
                    catch { }
                }
            }
            catch { }
        }

        return map;
    }

    // ── Parse /proc/net/{tcp,udp} ──────────────────────────────────────────────
    private void ParseProcNetFile(string path, ConnectionProtocol protocol,
                                  bool isUdp, List<NetworkConnection> result)
    {
        if (!File.Exists(path)) return;
        try
        {
            bool first = true;
            foreach (var line in File.ReadAllLines(path))
            {
                if (first) { first = false; continue; } // skip header
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                // sl local_address rem_address st tx_queue:rx_queue tr uid timeout inode ...
                if (parts.Length < 10) continue;

                var localHex  = parts[1];
                var remoteHex = parts[2];
                var stateHex  = parts[3];
                var inodeStr  = parts[9];

                if (!TryParseHexAddrPort(localHex,
                        protocol == ConnectionProtocol.Tcp6 || protocol == ConnectionProtocol.Udp6,
                        out var localAddr, out var localPort))
                    continue;
                if (!TryParseHexAddrPort(remoteHex,
                        protocol == ConnectionProtocol.Tcp6 || protocol == ConnectionProtocol.Udp6,
                        out var remoteAddr, out var remotePort))
                    continue;

                var state = isUdp ? TcpConnectionState.Unknown : ParseHexState(stateHex);

                int    pid     = 0;
                string procName = string.Empty;
                if (long.TryParse(inodeStr, out var inode) && _inodeMap.TryGetValue(inode, out var entry))
                {
                    pid      = entry.pid;
                    procName = entry.name;
                }

                result.Add(new NetworkConnection
                {
                    Protocol      = protocol,
                    LocalAddress  = localAddr,
                    LocalPort     = localPort,
                    RemoteAddress = remoteAddr,
                    RemotePort    = remotePort,
                    State         = state,
                    ProcessId     = pid,
                    ProcessName   = procName,
                });
            }
        }
        catch { }
    }

    private static bool TryParseHexAddrPort(string hexAddrPort, bool isIpv6,
                                             out string address, out int port)
    {
        address = string.Empty;
        port    = 0;

        var colonIdx = hexAddrPort.LastIndexOf(':');
        if (colonIdx < 0) return false;

        var addrHex = hexAddrPort[..colonIdx];
        var portHex = hexAddrPort[(colonIdx + 1)..];

        if (!int.TryParse(portHex, System.Globalization.NumberStyles.HexNumber, null, out port))
            return false;

        if (isIpv6)
        {
            if (addrHex.Length != 32) return false;
            var bytes = new byte[16];
            for (int g = 0; g < 4; g++)
                for (int b = 0; b < 4; b++)
                {
                    var hex = addrHex.Substring(g * 8 + (3 - b) * 2, 2);
                    if (!byte.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out bytes[g * 4 + b]))
                        return false;
                }
            address = new IPAddress(bytes).ToString();
        }
        else
        {
            if (addrHex.Length != 8) return false;
            if (!uint.TryParse(addrHex, System.Globalization.NumberStyles.HexNumber, null, out var ip32))
                return false;
            var bytes = new byte[4];
            bytes[0] = (byte)(ip32 & 0xFF);
            bytes[1] = (byte)((ip32 >> 8)  & 0xFF);
            bytes[2] = (byte)((ip32 >> 16) & 0xFF);
            bytes[3] = (byte)((ip32 >> 24) & 0xFF);
            address = new IPAddress(bytes).ToString();
        }

        return true;
    }

    private static TcpConnectionState ParseHexState(string hex)
    {
        if (!int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var v))
            return TcpConnectionState.Unknown;
        return v switch
        {
            0x01 => TcpConnectionState.Established,
            0x02 => TcpConnectionState.SynSent,
            0x03 => TcpConnectionState.SynReceived,
            0x04 => TcpConnectionState.FinWait1,
            0x05 => TcpConnectionState.FinWait2,
            0x06 => TcpConnectionState.TimeWait,
            0x07 => TcpConnectionState.Closed,
            0x08 => TcpConnectionState.CloseWait,
            0x09 => TcpConnectionState.LastAck,
            0x0A => TcpConnectionState.Listen,
            0x0B => TcpConnectionState.Closing,
            _    => TcpConnectionState.Unknown,
        };
    }
}
