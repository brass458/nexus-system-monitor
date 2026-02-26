using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Abstractions;

public interface IStartupProvider
{
    Task<IReadOnlyList<StartupItem>> GetStartupItemsAsync(CancellationToken ct = default);
    Task SetEnabledAsync(StartupItem item, bool enabled, CancellationToken ct = default);
}
