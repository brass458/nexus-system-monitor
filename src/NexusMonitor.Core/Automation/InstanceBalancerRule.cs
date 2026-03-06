namespace NexusMonitor.Core.Automation;

public enum BalancerAlgorithm
{
    SpreadEvenly,
    FixedCoreCount
}

/// <summary>
/// Distributes CPU affinity among multiple instances of the same process.
/// </summary>
public class InstanceBalancerRule
{
    public Guid              Id                 { get; set; } = Guid.NewGuid();
    public string            ProcessNamePattern { get; set; } = "";
    public BalancerAlgorithm Algorithm          { get; set; } = BalancerAlgorithm.SpreadEvenly;
    public int               CoresPerInstance   { get; set; } = 2;
    public bool              IsEnabled          { get; set; } = true;
}
