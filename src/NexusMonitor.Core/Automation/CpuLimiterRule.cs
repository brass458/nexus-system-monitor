namespace NexusMonitor.Core.Automation;

/// <summary>
/// Defines when and how to temporarily reduce a process's CPU affinity when
/// it exceeds a CPU threshold for a sustained period.
/// </summary>
public class CpuLimiterRule
{
    public Guid   Id                   { get; set; } = Guid.NewGuid();
    public string ProcessNamePattern   { get; set; } = "";
    public double CpuThresholdPercent  { get; set; } = 80.0;
    public int    OverLimitSeconds     { get; set; } = 5;
    public int    ReduceByCores        { get; set; } = 2;
    public int    LimitDurationSeconds { get; set; } = 30;
    public bool   IsEnabled            { get; set; } = true;
}
