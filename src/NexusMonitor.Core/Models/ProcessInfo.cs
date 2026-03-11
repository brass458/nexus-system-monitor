using NexusMonitor.Core.Abstractions;

namespace NexusMonitor.Core.Models;

public enum ProcessCategory
{
    SystemKernel,   // OS kernel / protected system process
    WindowsService, // Windows/launchd/systemd service
    UserApplication,
    DotNetManaged,
    Suspicious,     // High entropy, packed binary
    Suspended,
    GpuAccelerated,
    CurrentProcess
}

public enum ProcessState
{
    Running,
    Suspended,
    Zombie,
    Unknown
}

public record ProcessInfo
{
    public int Pid { get; init; }
    public int ParentPid { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ImagePath { get; init; } = string.Empty;
    public string CommandLine { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public ProcessCategory Category { get; init; }
    public ProcessState State { get; init; }
    public DateTime StartTime { get; init; }

    // CPU
    public double CpuPercent { get; init; }
    public int ThreadCount { get; init; }
    public int HandleCount { get; init; }

    // Memory (bytes)
    public long WorkingSetBytes { get; init; }
    public long PrivateBytesBytes { get; init; }
    public long PagedPoolBytes { get; init; }
    public long VirtualBytesBytes { get; init; }

    // I/O (bytes/sec)
    public long IoReadBytesPerSec { get; init; }
    public long IoWriteBytesPerSec { get; init; }

    // GPU
    public double GpuPercent { get; init; }

    // Network (bytes/sec)
    public long NetworkSendBytesPerSec { get; init; }
    public long NetworkRecvBytesPerSec { get; init; }

    // Flags
    public bool IsElevated { get; init; }
    public bool IsCritical { get; init; }
    public bool AccessDenied { get; init; }

    // Extended (Phase 7)
    public long AffinityMask { get; init; }
    public IoPriority CurrentIoPriority { get; init; }
    public MemoryPriority CurrentMemoryPriority { get; init; }
    public bool IsEfficiencyMode { get; init; }

    // Priority / nice value (0 on Windows = Normal; on Linux = nice value -20..19)
    public int BasePriority { get; init; }
}
