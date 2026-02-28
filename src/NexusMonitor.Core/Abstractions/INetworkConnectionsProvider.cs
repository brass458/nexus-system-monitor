using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Abstractions;

public interface INetworkConnectionsProvider
{
    IObservable<IReadOnlyList<NetworkConnection>> GetConnectionStream(TimeSpan interval);
    Task<IReadOnlyList<NetworkConnection>> GetConnectionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Emits aggregate adapter-level send/receive rates (all non-loopback adapters combined).
    /// Does not require elevated privileges.
    /// </summary>
    IObservable<AdapterThroughput> GetAdapterThroughputStream(TimeSpan interval);
}
