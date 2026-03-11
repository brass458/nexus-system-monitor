namespace NexusMonitor.Core.Health;

public record MemoryLeakSuspect
{
    public int Pid { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public double LeakRateBytesPerHour { get; init; }
    public double HandleLeakRatePerHour { get; init; }
    public double Confidence { get; init; }
    public int ObservationWindowSeconds { get; init; }
    public DateTimeOffset FirstDetected { get; init; }
    public IReadOnlyList<double> WorkingSetHistory { get; init; } = [];
    public IReadOnlyList<double> HandleHistory { get; init; } = [];
}
