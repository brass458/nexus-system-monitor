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

    // ── Extended detail (Task Manager parity) ──────────────────────────────────
    public double BaseSpeedMhz { get; init; }
    public int    Sockets { get; init; } = 1;
    public string VirtualizationStatus { get; init; } = string.Empty; // "Enabled", "Disabled", etc.
    public long   L1CacheBytes { get; init; }
    public long   L2CacheBytes { get; init; }
    public long   L3CacheBytes { get; init; }

    // System-wide counts (shown on CPU detail in Task Manager)
    public int ProcessCount { get; init; }
    public int ThreadCount  { get; init; }
    public int HandleCount  { get; init; }
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

    // ── Extended detail ────────────────────────────────────────────────────────
    public int    SpeedMhz { get; init; }
    public int    SlotsUsed { get; init; }
    public int    TotalSlots { get; init; }
    public string FormFactor { get; init; } = string.Empty; // "DIMM", "SODIMM", etc.
    public long   HardwareReservedBytes { get; init; }
    public long   CompressedBytes { get; init; }
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

    // ── Extended detail ────────────────────────────────────────────────────────
    public string DiskType { get; init; } = string.Empty;        // "NVMe", "SSD", "HDD"
    public string FormattedCapacity { get; init; } = string.Empty;
    public double AverageResponseMs { get; init; }
    public bool   IsSystemDisk { get; init; }
    public bool   HasPageFile { get; init; }

    // Per-volume data for this physical disk
    public IReadOnlyList<VolumeInfo> Volumes { get; init; } = [];
}

public record VolumeInfo
{
    public string DriveLetter { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string FileSystem { get; init; } = string.Empty;  // "NTFS", "FAT32", "ReFS"
    public long   TotalBytes { get; init; }
    public long   FreeBytes { get; init; }
    public double UsedPercent => TotalBytes > 0 ? (double)(TotalBytes - FreeBytes) / TotalBytes * 100.0 : 0;
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

    // ── Extended detail ────────────────────────────────────────────────────────
    public string DnsSuffix { get; init; } = string.Empty;
    public string ConnectionType { get; init; } = string.Empty; // "Wi-Fi", "Ethernet", "Bluetooth"
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

    // ── Extended detail ────────────────────────────────────────────────────────
    public long   SharedMemoryTotalBytes { get; init; }
    public string DriverVersion { get; init; } = string.Empty;
    public string DirectXVersion { get; init; } = string.Empty; // "DirectX 12", etc.
    public string PhysicalLocation { get; init; } = string.Empty; // "PCI bus 1, device 0..."
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
