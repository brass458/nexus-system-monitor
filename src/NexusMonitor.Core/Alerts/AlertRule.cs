namespace NexusMonitor.Core.Alerts;

public class AlertRule
{
    public Guid          Id          { get; set; } = Guid.NewGuid();
    public string        Name        { get; set; } = "New Alert";
    public bool          IsEnabled   { get; set; } = true;
    public AlertMetric   Metric      { get; set; } = AlertMetric.CpuPercent;
    public double        Threshold   { get; set; } = 90.0;
    public AlertSeverity Severity    { get; set; } = AlertSeverity.Warning;
    /// <summary>Seconds the metric must stay above threshold before firing.</summary>
    public int           SustainSec  { get; set; } = 5;
    /// <summary>Minimum seconds between repeated alerts for this rule.</summary>
    public int           CooldownSec { get; set; } = 60;
}
