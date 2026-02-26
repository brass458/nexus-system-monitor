namespace NexusMonitor.Core.Models;

public record CpuMetrics
{
    public double TotalPercent { get; init; }
    public IReadOnlyList<double> CorePercents { get; init; } = [];
    public double FrequencyMhz { get; init; }
    public double TemperatureCelsius { get; init; }
    public int LogicalCores { get; init; }
    public int PhysicalCores { get; init; }
    public string ModelName { get; init; } = string.Empty;
}

public record MemoryMetrics
{
    public long TotalBytes { get; init; }
    public long UsedBytes { get; init; }
    public long AvailableBytes { get; init; }
    public long CachedBytes { get; init; }
    public long PagedPoolBytes { get; init; }
    public long NonPagedPoolBytes { get; init; }
    public long CommitTotalBytes { get; init; }
    public long CommitLimitBytes { get; init; }
    public double UsedPercent => TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100.0 : 0;
}

public record DiskMetrics
{
    public string DriveLetter { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public long ReadBytesPerSec { get; init; }
    public long WriteBytesPerSec { get; init; }
    public double ActivePercent { get; init; }
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
    public double UsedPercent => TotalBytes > 0 ? (double)(TotalBytes - FreeBytes) / TotalBytes * 100.0 : 0;
    public int    DiskIndex       { get; init; }
    public string PhysicalName    { get; init; } = string.Empty;
    public string AllDriveLetters { get; init; } = string.Empty;
}

public record NetworkAdapterMetrics
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public long SendBytesPerSec { get; init; }
    public long RecvBytesPerSec { get; init; }
    public long TotalSendBytes { get; init; }
    public long TotalRecvBytes { get; init; }
    public bool IsConnected { get; init; }
    public string IpAddress { get; init; } = string.Empty;
    public string IPv4Address  { get; init; } = string.Empty;
    public string IPv6Address  { get; init; } = string.Empty;
    public long   LinkSpeedBps { get; init; }
    public string AdapterType  { get; init; } = string.Empty;
}

public record GpuMetrics
{
    public string Name { get; init; } = string.Empty;
    public double UsagePercent { get; init; }
    public long DedicatedMemoryUsedBytes { get; init; }
    public long DedicatedMemoryTotalBytes { get; init; }
    public double TemperatureCelsius { get; init; }
    public double MemoryUsedPercent => DedicatedMemoryTotalBytes > 0
        ? (double)DedicatedMemoryUsedBytes / DedicatedMemoryTotalBytes * 100.0 : 0;
    public double Engine3DPercent          { get; init; }
    public double EngineCopyPercent        { get; init; }
    public double EngineVideoDecodePercent { get; init; }
    public double EngineVideoEncodePercent { get; init; }
    public long   SharedMemoryUsedBytes    { get; init; }
}

public record SystemMetrics
{
    public CpuMetrics Cpu { get; init; } = new();
    public MemoryMetrics Memory { get; init; } = new();
    public IReadOnlyList<DiskMetrics> Disks { get; init; } = [];
    public IReadOnlyList<NetworkAdapterMetrics> NetworkAdapters { get; init; } = [];
    public IReadOnlyList<GpuMetrics> Gpus { get; init; } = [];
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
