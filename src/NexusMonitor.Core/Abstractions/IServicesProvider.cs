using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Abstractions;

public interface IServicesProvider
{
    Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(CancellationToken ct = default);
    Task StartServiceAsync(string name, CancellationToken ct = default);
    Task StopServiceAsync(string name, CancellationToken ct = default);
    Task RestartServiceAsync(string name, CancellationToken ct = default);
    Task SetStartTypeAsync(string name, ServiceStartType startType, CancellationToken ct = default);
}
