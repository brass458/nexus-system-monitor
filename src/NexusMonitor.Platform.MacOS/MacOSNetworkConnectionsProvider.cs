using System.Diagnostics;
using System.Reactive.Linq;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Helpers;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Platform.MacOS;

public sealed class MacOSNetworkConnectionsProvider : INetworkConnectionsProvider
{
    private readonly AdapterThroughputTracker _adapterTracker = new();

    public IObservable<IReadOnlyList<NetworkConnection>> GetConnectionStream(TimeSpan interval) =>
        Observable.Timer(TimeSpan.Zero, interval)
                  .Select(_ => (IReadOnlyList<NetworkConnection>)GetConnections());

    public Task<IReadOnlyList<NetworkConnection>> GetConnectionsAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<NetworkConnection>>(GetConnections, ct);

    public IObservable<AdapterThroughput> GetAdapterThroughputStream(TimeSpan interval) =>
        Observable.Timer(TimeSpan.Zero, interval)
                  .Select(_ => _adapterTracker.Sample());

    private static IReadOnlyList<NetworkConnection> GetConnections()
    {
        var result = new List<NetworkConnection>();
        try
        {
            // netstat -anvp tcp  (TCP connections with PID)
            ParseNetstat("tcp",  ConnectionProtocol.Tcp4, result);
            ParseNetstat("tcp6", ConnectionProtocol.Tcp6, result);
            ParseNetstat("udp",  ConnectionProtocol.Udp4, result);
            ParseNetstat("udp6", ConnectionProtocol.Udp6, result);
        }
        catch { }

        return result;
    }

    private static void ParseNetstat(string proto, ConnectionProtocol protocol,
                                     List<NetworkConnection> result)
    {
        var output = RunNetstat(proto);
        if (string.IsNullOrEmpty(output)) return;

        bool isUdp = proto.StartsWith("udp", StringComparison.OrdinalIgnoreCase);

        foreach (var line in output.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // macOS netstat -anvp tcp output columns:
            // Proto Recv-Q Send-Q Local-Address Foreign-Address (state) rhiwat shiwat pid
            // For udp there is no state column — at least 7 fields for pid to be last non-flag
            if (parts.Length < 8) continue;

            string localAddr, remoteAddr;
            TcpConnectionState state;
            int pid = 0;

            if (isUdp)
            {
                // Proto Recv-Q Send-Q Local Foreign [rhiwat shiwat] pid
                localAddr  = parts[3];
                remoteAddr = parts[4];
                state      = TcpConnectionState.Unknown;
                // pid is typically the last or second-to-last token
                _ = int.TryParse(parts[^1], out pid);
            }
            else
            {
                // Proto Recv-Q Send-Q Local Foreign State rhiwat shiwat pid
                if (parts.Length < 9) continue;
                localAddr  = parts[3];
                remoteAddr = parts[4];
                state      = ParseTcpState(parts[5]);
                _ = int.TryParse(parts[^1], out pid);
            }

            SplitAddressPort(localAddr,  out var lAddr, out var lPort);
            SplitAddressPort(remoteAddr, out var rAddr, out var rPort);

            result.Add(new NetworkConnection
            {
                Protocol      = protocol,
                LocalAddress  = lAddr,
                LocalPort     = lPort,
                RemoteAddress = rAddr,
                RemotePort    = rPort,
                State         = state,
                ProcessId     = pid,
                ProcessName   = string.Empty,
            });
        }
    }

    private static string RunNetstat(string proto)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo("netstat", $"-anvp {proto}")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                }
            };
            proc.Start();
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void SplitAddressPort(string addrPort, out string address, out int port)
    {
        address = addrPort;
        port    = 0;
        if (string.IsNullOrEmpty(addrPort)) return;

        // IPv6 addresses are enclosed in brackets: [::1].port
        if (addrPort.StartsWith('['))
        {
            var closeBracket = addrPort.IndexOf(']');
            if (closeBracket >= 0)
            {
                address = addrPort[1..closeBracket];
                if (closeBracket + 1 < addrPort.Length && addrPort[closeBracket + 1] == '.')
                    int.TryParse(addrPort[(closeBracket + 2)..], out port);
                return;
            }
        }

        // IPv4 or hostname: last '.' separates port on macOS netstat
        var lastDot = addrPort.LastIndexOf('.');
        if (lastDot >= 0)
        {
            address = addrPort[..lastDot];
            int.TryParse(addrPort[(lastDot + 1)..], out port);
        }
    }

    private static TcpConnectionState ParseTcpState(string s) => s.ToUpperInvariant() switch
    {
        "ESTABLISHED" => TcpConnectionState.Established,
        "LISTEN"      => TcpConnectionState.Listen,
        "SYN_SENT"    => TcpConnectionState.SynSent,
        "SYN_RCVD"    => TcpConnectionState.SynReceived,
        "FIN_WAIT_1"  => TcpConnectionState.FinWait1,
        "FIN_WAIT_2"  => TcpConnectionState.FinWait2,
        "CLOSE_WAIT"  => TcpConnectionState.CloseWait,
        "CLOSING"     => TcpConnectionState.Closing,
        "LAST_ACK"    => TcpConnectionState.LastAck,
        "TIME_WAIT"   => TcpConnectionState.TimeWait,
        "CLOSED"      => TcpConnectionState.Closed,
        _             => TcpConnectionState.Unknown,
    };
}
