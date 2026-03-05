namespace NexusMonitor.Core.Models;

/// <summary>Which system resource an incident is attributed to.</summary>
public enum ResourceType
{
    Cpu     = 0,
    Ram     = 1,
    Gpu     = 2,
    Vram    = 3,
    Disk    = 4,
}

/// <summary>Root-cause classification for a resource incident.</summary>
public enum EventClassification
{
    /// <summary>System-wide, sustained saturation that no single app explains.</summary>
    HardwareBottleneck = 0,
    /// <summary>Single application caused a transient spike (e.g., game startup).</summary>
    ApplicationSpike   = 1,
    /// <summary>Single application's memory grew monotonically over the incident window.</summary>
    ApplicationLeak    = 2,
    /// <summary>Temperature-driven performance reduction detected (thermal throttle).</summary>
    ThermalThrottle    = 3,
    Unknown            = 4,
}

/// <summary>
/// A classified resource incident: a period where a resource (CPU, RAM, GPU…) exceeded
/// a threshold, enriched with process attribution and root-cause classification.
/// </summary>
public sealed class ResourceEvent
{
    public long                Id                  { get; set; }
    public DateTime            Timestamp           { get; set; }
    public DateTime?           EndTimestamp        { get; set; }
    public ResourceType        Resource            { get; set; }
    public double              PeakUsagePercent    { get; set; }
    public double              AverageUsagePercent { get; set; }
    public TimeSpan            Duration            { get; set; }
    /// <summary>Name of the top-consuming process at the time of the incident.</summary>
    public string              PrimaryProcess      { get; set; } = string.Empty;
    public int                 PrimaryProcessPid   { get; set; }
    public EventClassification Classification      { get; set; }
    /// <summary>Uses <see cref="NexusMonitor.Core.Storage.EventSeverity"/> constants.</summary>
    public int                 Severity            { get; set; }
    public string              Summary             { get; set; } = string.Empty;
}
