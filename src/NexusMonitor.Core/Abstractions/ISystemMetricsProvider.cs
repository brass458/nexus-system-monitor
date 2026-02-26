using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Abstractions;

public interface ISystemMetricsProvider
{
    IObservable<SystemMetrics> GetMetricsStream(TimeSpan interval);
    Task<SystemMetrics> GetMetricsAsync(CancellationToken ct = default);
}
