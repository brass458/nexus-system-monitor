namespace NexusMonitor.Core.Alerts;

public record AlertEvent(
    AlertRule Rule,
    double    Value,        // actual measured value at the time of firing
    DateTime  Timestamp)
{
    public string TimeDisplay  => Timestamp.ToLocalTime().ToString("HH:mm:ss");
    public string ValueDisplay => Rule.Metric switch
    {
        AlertMetric.CpuTemperature => $"{Value:F0} \u00b0C",
        _                          => $"{Value:F1}%"
    };
    public string SeverityIcon  => Rule.Severity switch
    {
        AlertSeverity.Critical => "\U0001f534",
        AlertSeverity.Warning  => "\U0001f7e0",
        _                      => "\U0001f535"
    };
    public string SeverityColor => Rule.Severity switch
    {
        AlertSeverity.Critical => "#FF453A",
        AlertSeverity.Warning  => "#FF9F0A",
        _                      => "#0A84FF"
    };
    public string Description => $"{Rule.Name}: {Rule.Metric} = {ValueDisplay} (threshold: {Rule.Threshold:F0}{(Rule.Metric == AlertMetric.CpuTemperature ? "\u00b0C" : "%")})";
}
