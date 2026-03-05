namespace NexusMonitor.Core.Health;

public enum HealthLevel { Excellent, Good, Fair, Poor, Critical }
public enum TrendDirection { Improving, Stable, Degrading }

public record SubsystemHealth
{
    public string Name { get; init; } = string.Empty;
    /// <summary>0-100 score (higher = healthier).</summary>
    public double Score { get; init; }
    public HealthLevel Level { get; init; }
    public TrendDirection Trend { get; init; }
    /// <summary>Current raw value (e.g. CPU%, Memory%).</summary>
    public double CurrentValue { get; init; }
    /// <summary>Human-readable summary, e.g. "72% used".</summary>
    public string Summary { get; init; } = string.Empty;
}

public record ProcessImpact
{
    public int    Pid         { get; init; }
    public string Name        { get; init; } = string.Empty;
    /// <summary>0-100 composite impact score.</summary>
    public double ImpactScore { get; init; }
    public double CpuPercent  { get; init; }
    public long   MemoryBytes { get; init; }
}

public record SystemHealthSnapshot
{
    public HealthLevel         OverallHealth    { get; init; }
    /// <summary>0-100 composite score.</summary>
    public double              OverallScore     { get; init; }
    public TrendDirection      OverallTrend     { get; init; }
    public SubsystemHealth     Cpu              { get; init; } = new();
    public SubsystemHealth     Memory           { get; init; } = new();
    public SubsystemHealth     Disk             { get; init; } = new();
    public SubsystemHealth     Gpu              { get; init; } = new();
    public IReadOnlyList<ProcessImpact> TopConsumers { get; init; } = [];
    public int                 ActiveAutomations { get; init; }
    public BottleneckReport?   Bottleneck        { get; init; }
    public DateTime            Timestamp         { get; init; } = DateTime.UtcNow;
}
