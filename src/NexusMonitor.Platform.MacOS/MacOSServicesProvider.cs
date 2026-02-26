using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Platform.MacOS;

public sealed class MacOSServicesProvider : IServicesProvider
{
    public Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ServiceInfo>>([]);

    public Task StartServiceAsync(string name, CancellationToken ct = default) =>
        throw new PlatformNotSupportedException("launchd service control not yet implemented.");

    public Task StopServiceAsync(string name, CancellationToken ct = default) =>
        throw new PlatformNotSupportedException("launchd service control not yet implemented.");

    public Task RestartServiceAsync(string name, CancellationToken ct = default) =>
        throw new PlatformNotSupportedException("launchd service control not yet implemented.");

    public Task SetStartTypeAsync(string name, ServiceStartType startType, CancellationToken ct = default) =>
        throw new PlatformNotSupportedException("launchd service control not yet implemented.");
}
