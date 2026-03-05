namespace NexusMonitor.Core.Health;

public enum BottleneckType
{
    /// <summary>No significant workload detected — system is largely idle.</summary>
    Idle,
    /// <summary>All components are roughly equally utilised — well-matched hardware.</summary>
    Balanced,
    /// <summary>GPU compute is maxed; CPU has remaining headroom.</summary>
    GpuBound,
    /// <summary>GPU VRAM is exhausted; spilling to system RAM or dropping assets.</summary>
    VramBound,
    /// <summary>CPU is maxed; GPU has remaining headroom (common in CPU-heavy games/simulations).</summary>
    CpuBound,
    /// <summary>RAM capacity is nearly full; system is paging to disk.</summary>
    MemoryBound,
    /// <summary>Disk I/O is the limiting factor (asset streaming, level loads, swap).</summary>
    StorageBound,
    /// <summary>CPU or GPU is thermally throttling — hardware is artificially slowing itself down to avoid damage.</summary>
    ThermalThrottle,
}

public enum WorkloadType
{
    Unknown,
    Gaming,
    Streaming,
    VideoEditing,
    ThreeDRendering,
    CadEngineering,
    GeneralCompute,
}

public enum BottleneckSeverity
{
    /// <summary>Mild imbalance — noticeable but not crippling.</summary>
    Mild,
    /// <summary>Significant imbalance — clear performance is being left on the table.</summary>
    Moderate,
    /// <summary>Severe imbalance — one component is definitively the limiting factor.</summary>
    Severe,
}

/// <summary>
/// A plain-English bottleneck verdict for the current workload and hardware state.
/// Updated every tick by <see cref="BottleneckDetector"/>.
/// </summary>
public record BottleneckReport
{
    public BottleneckType     Bottleneck       { get; init; }
    public BottleneckSeverity Severity         { get; init; }
    public WorkloadType       Workload         { get; init; }
    /// <summary>Name of the primary workload process (e.g. "Cyberpunk2077", "obs64").</summary>
    public string             WorkloadProcess  { get; init; } = string.Empty;

    /// <summary>Short one-line verdict, e.g. "GPU bottleneck detected".</summary>
    public string Headline { get; init; } = string.Empty;
    /// <summary>
    /// 2–3 sentence plain-English explanation of what is happening and what it means for performance.
    /// </summary>
    public string Explanation { get; init; } = string.Empty;
    /// <summary>Actionable upgrade or tuning advice. Empty when the system is balanced or idle.</summary>
    public string UpgradeAdvice { get; init; } = string.Empty;

    // ── Supporting evidence shown in the detail card ────────────────────────
    public double CpuPercent         { get; init; }
    public double GpuPercent         { get; init; }
    public double GpuVramPercent     { get; init; }
    public double MemoryPercent      { get; init; }
    public double DiskPercent        { get; init; }
    public double CpuTempCelsius     { get; init; }
    public double GpuTempCelsius     { get; init; }
    /// <summary>True when CPU frequency is detectably below its rated speed (thermal throttle indicator).</summary>
    public bool   CpuIsThrottling    { get; init; }

    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
