namespace NexusMonitor.Core.Storage;

/// <summary>
/// Read-only query API over the metrics database.
/// Implemented by MetricsStore; injectable as a separate interface for ViewModels
/// that only need to read historical data (not control the write pipeline).
/// </summary>
public interface IMetricsReader
{
    /// <summary>
    /// Returns system metrics for the given time range.
    /// Automatically selects the best available granularity:
    ///   &lt; 2 hours  → raw 1-second data
    ///   &lt; 24 hours → 1-minute rollups
    ///   &lt; 7 days   → 5-minute rollups
    ///   ≥ 7 days   → 1-hour rollups
    /// </summary>
    Task<IReadOnlyList<MetricsDataPoint>> GetSystemMetricsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>Returns process-level history for a named process over the given range.</summary>
    Task<IReadOnlyList<ProcessDataPoint>> GetProcessHistoryAsync(
        string processName, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>Returns network connection snapshots, optionally filtered by remote address/port.</summary>
    Task<IReadOnlyList<NetworkDataPoint>> GetNetworkHistoryAsync(
        string? remoteAddress, int? remotePort,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>Returns stored events in the given range, optionally filtered by event type.</summary>
    Task<IReadOnlyList<StoredEvent>> GetEventsAsync(
        DateTimeOffset from, DateTimeOffset to,
        string? eventType = null, CancellationToken ct = default);

    /// <summary>Returns the oldest and newest timestamps with data in the database.</summary>
    Task<(DateTimeOffset oldest, DateTimeOffset newest)> GetDataRangeAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Returns the top N processes by average CPU in the given time range,
    /// aggregated from the process_snapshots table.
    /// </summary>
    Task<IReadOnlyList<ProcessSummary>> GetTopProcessSummariesAsync(
        DateTimeOffset from, DateTimeOffset to, int topN = 10, CancellationToken ct = default);

    /// <summary>Returns the current size of the metrics.db file in bytes.</summary>
    long GetDatabaseSizeBytes();
}
