namespace NexusMonitor.Core.Storage;

/// <summary>One time-series point from system_metrics or a rollup table.</summary>
public record MetricsDataPoint(
    DateTimeOffset Timestamp,
    double         CpuPercent,
    double?        CpuMaxPercent,
    long           MemUsedBytes,
    long?          MemMaxBytes,
    long           DiskReadBps,
    long           DiskWriteBps,
    long           NetSendBps,
    long           NetRecvBps,
    double         GpuPercent,
    double?        GpuMaxPercent,
    int            SampleCount);

/// <summary>One process snapshot row from process_snapshots.</summary>
public record ProcessDataPoint(
    DateTimeOffset Timestamp,
    int            Pid,
    string         Name,
    double         CpuPercent,
    long           MemBytes,
    long           IoReadBps,
    long           IoWriteBps,
    double         GpuPercent);

/// <summary>One network connection snapshot row from network_snapshots.</summary>
public record NetworkDataPoint(
    DateTimeOffset Timestamp,
    int            Protocol,
    string         LocalAddr,
    int            LocalPort,
    string         RemoteAddr,
    int            RemotePort,
    int            State,
    int            Pid,
    string         ProcessName,
    long           SendBps,
    long           RecvBps);

/// <summary>One event row from the events table.</summary>
public record StoredEvent(
    long           Id,
    DateTimeOffset Timestamp,
    string         EventType,
    int            Severity,
    string?        MetricName,
    double?        MetricValue,
    double?        Threshold,
    string?        Description,
    string?        MetadataJson);

/// <summary>Aggregate summary for a process over a queried time range.</summary>
public record ProcessSummary(
    string Name,
    double AvgCpuPercent,
    double PeakCpuPercent,
    double AvgMemMb);

/// <summary>Event severity constants for use when writing to the events table.</summary>
public static class EventSeverity
{
    public const int Info     = 0;
    public const int Warning  = 1;
    public const int Critical = 2;
}

/// <summary>Well-known event type strings.</summary>
public static class EventType
{
    public const string CpuHigh       = "cpu_high";
    public const string MemHigh       = "mem_high";
    public const string GpuHigh       = "gpu_high";
    public const string NetAnomaly    = "net_anomaly";
    public const string NewConnection = "new_connection";
    public const string ProcessSpike  = "process_spike";
}
