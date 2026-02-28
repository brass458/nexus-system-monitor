using System.Net;
using System.Reactive.Linq;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Platform.Linux;

public sealed class LinuxNetworkConnectionsProvider : INetworkConnectionsProvider
{
    public IObservable<IReadOnlyList<NetworkConnection>> GetConnectionStream(TimeSpan interval) =>
        Observable.Timer(TimeSpan.Zero, interval)
                  .Select(_ => (IReadOnlyList<NetworkConnection>)GetConnections());

    public Task<IReadOnlyList<NetworkConnection>> GetConnectionsAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<NetworkConnection>>(GetConnections, ct);

    private static IReadOnlyList<NetworkConnection> GetConnections()
    {
        var result = new List<NetworkConnection>();

        ParseProcNetFile("/proc/net/tcp",  ConnectionProtocol.Tcp4, false, result);
        ParseProcNetFile("/proc/net/tcp6", ConnectionProtocol.Tcp6, false, result);
        ParseProcNetFile("/proc/net/udp",  ConnectionProtocol.Udp4, true,  result);
        ParseProcNetFile("/proc/net/udp6", ConnectionProtocol.Udp6, true,  result);

        // Enrich with process names from /proc/net/tcp inode → /proc/[pid]/fd
        // Skipped for performance — process names left empty

        return result;
    }

    private static void ParseProcNetFile(string path, ConnectionProtocol protocol,
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
                // Minimum 10 fields
                if (parts.Length < 10) continue;

                var localHex  = parts[1];
                var remoteHex = parts[2];
                var stateHex  = parts[3];

                if (!TryParseHexAddrPort(localHex,  protocol == ConnectionProtocol.Tcp6 || protocol == ConnectionProtocol.Udp6,
                                         out var localAddr, out var localPort))
                    continue;
                if (!TryParseHexAddrPort(remoteHex, protocol == ConnectionProtocol.Tcp6 || protocol == ConnectionProtocol.Udp6,
                                         out var remoteAddr, out var remotePort))
                    continue;

                var state = isUdp
                    ? TcpConnectionState.Unknown
                    : ParseHexState(stateHex);

                result.Add(new NetworkConnection
                {
                    Protocol      = protocol,
                    LocalAddress  = localAddr,
                    LocalPort     = localPort,
                    RemoteAddress = remoteAddr,
                    RemotePort    = remotePort,
                    State         = state,
                    ProcessId     = 0,  // inode resolution skipped for performance
                    ProcessName   = string.Empty,
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
            // IPv6: 32 hex chars in little-endian 4-byte groups
            if (addrHex.Length != 32) return false;
            var bytes = new byte[16];
            for (int g = 0; g < 4; g++)
            {
                for (int b = 0; b < 4; b++)
                {
                    var hex = addrHex.Substring(g * 8 + (3 - b) * 2, 2);
                    if (!byte.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out bytes[g * 4 + b]))
                        return false;
                }
            }
            address = new IPAddress(bytes).ToString();
        }
        else
        {
            // IPv4: 8 hex chars little-endian 32-bit
            if (addrHex.Length != 8) return false;
            if (!uint.TryParse(addrHex, System.Globalization.NumberStyles.HexNumber, null, out var ip32))
                return false;
            // Convert little-endian to bytes
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
