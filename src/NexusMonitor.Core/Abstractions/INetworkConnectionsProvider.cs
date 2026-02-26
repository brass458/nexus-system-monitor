using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Abstractions;

public interface INetworkConnectionsProvider
{
    IObservable<IReadOnlyList<NetworkConnection>> GetConnectionStream(TimeSpan interval);
    Task<IReadOnlyList<NetworkConnection>> GetConnectionsAsync(CancellationToken ct = default);
}
