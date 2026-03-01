namespace NexusMonitor.Core.Storage;

/// <summary>
/// Write-only interface for the events table. Implemented by <see cref="MetricsStore"/>.
/// Inject this into services that detect anomalies so they can persist events
/// without depending on the full MetricsStore.
/// </summary>
public interface IEventWriter
{
    Task InsertEventAsync(
        string  eventType,
        int     severity,
        string? metricName,
        double? metricValue,
        double? threshold,
        string? description,
        string? metadataJson = null);
}
