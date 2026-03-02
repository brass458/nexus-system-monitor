using System.Reactive.Linq;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Mock;

public sealed class MockNetworkConnectionsProvider : INetworkConnectionsProvider
{
    private static readonly IReadOnlyList<NetworkConnection> _mock =
    [
        new() { Protocol = ConnectionProtocol.Tcp4, LocalAddress = "0.0.0.0",     LocalPort = 443,   RemoteAddress = "0.0.0.0",       RemotePort = 0,   State = TcpConnectionState.Listen,      ProcessId = 4,    ProcessName = "System"  },
        new() { Protocol = ConnectionProtocol.Tcp4, LocalAddress = "127.0.0.1",   LocalPort = 3306,  RemoteAddress = "0.0.0.0",       RemotePort = 0,   State = TcpConnectionState.Listen,      ProcessId = 1234, ProcessName = "mysqld"  },
        new() { Protocol = ConnectionProtocol.Tcp4, LocalAddress = "192.168.1.5", LocalPort = 50021, RemoteAddress = "52.96.36.162",  RemotePort = 443, State = TcpConnectionState.Established, ProcessId = 5678, ProcessName = "chrome"  },
        new() { Protocol = ConnectionProtocol.Tcp4, LocalAddress = "192.168.1.5", LocalPort = 50045, RemoteAddress = "142.250.80.46", RemotePort = 443, State = TcpConnectionState.Established, ProcessId = 5678, ProcessName = "chrome"  },
        new() { Protocol = ConnectionProtocol.Udp4, LocalAddress = "0.0.0.0",     LocalPort = 5353,  RemoteAddress = "—",             RemotePort = 0,   State = TcpConnectionState.Unknown,     ProcessId = 4,    ProcessName = "System"  },
        new() { Protocol = ConnectionProtocol.Tcp6, LocalAddress = "::",          LocalPort = 135,   RemoteAddress = "::",            RemotePort = 0,   State = TcpConnectionState.Listen,      ProcessId = 4,    ProcessName = "System"  },
    ];

    public bool SupportsPerConnectionThroughput => false;

    public IObservable<IReadOnlyList<NetworkConnection>> GetConnectionStream(TimeSpan interval) =>
        Observable.Timer(TimeSpan.Zero, interval).Select(_ => _mock);

    public Task<IReadOnlyList<NetworkConnection>> GetConnectionsAsync(CancellationToken ct = default) =>
        Task.FromResult(_mock);

    public IObservable<AdapterThroughput> GetAdapterThroughputStream(TimeSpan interval) =>
        Observable.Return(AdapterThroughput.Zero);
}
