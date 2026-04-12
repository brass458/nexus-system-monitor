namespace NexusMonitor.Core.Storage;

public sealed record HealthDataPoint(
    DateTimeOffset Timestamp,
    double Overall,
    double Cpu,
    double Memory,
    double Disk,
    double Gpu,
    string? Bottleneck);
