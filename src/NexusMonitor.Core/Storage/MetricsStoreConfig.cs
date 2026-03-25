namespace NexusMonitor.Core.Storage;

public sealed class MetricsStoreConfig
{
    /// <summary>Number of top consumers (by CPU + memory) to store per tick.</summary>
    public int TopNProcesses { get; set; } = 15;

    /// <summary>Maximum network connections to persist per tick. Excess are dropped.</summary>
    public int NetworkSnapshotMaxRows { get; set; } = 200;

    /// <summary>Whether to record network connection snapshots at all.</summary>
    public bool RecordNetworkSnapshots { get; set; } = true;

    /// <summary>Accumulate this many ticks before flushing in a single transaction.</summary>
    public int WriteBufferSize { get; set; } = 30;

    /// <summary>
    /// Hard cap on the metrics write buffer. When buffer Count exceeds this,
    /// the oldest entry is dropped and a warning is logged.
    /// Prevents OOM if the flush stalls. Set to 0 to compute as 3 × WriteBufferSize.
    /// </summary>
    public int MaxBufferSize { get; set; } = 0;

    // ── Retention ──────────────────────────────────────────────────────────────
    public TimeSpan RawRetention      { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan Rollup1mRetention { get; set; } = TimeSpan.FromDays(7);
    public TimeSpan Rollup5mRetention { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan Rollup1hRetention { get; set; } = TimeSpan.FromDays(365);

    /// <summary>How long to keep rows in the events table before pruning.</summary>
    public TimeSpan EventsRetention   { get; set; } = TimeSpan.FromDays(90);
}
