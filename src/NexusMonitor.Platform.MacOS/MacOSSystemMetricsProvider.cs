using System.Runtime.InteropServices;
using System.Reactive.Linq;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Platform.MacOS;

// P/Invoke wrapper for libSystem — must be partial for [LibraryImport] source generation
internal static partial class LibSystem
{
    [LibraryImport("libSystem.B.dylib", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int sysctlbyname(
        string name, [Out] byte[] oldp, ref nuint oldlenp, nint newp, nuint newlen);
}

public sealed class MacOSSystemMetricsProvider : ISystemMetricsProvider
{
    // ── sysctl helpers ─────────────────────────────────────────────────────────
    private static string SysctlString(string name)
    {
        nuint size = 256;
        var buf = new byte[size];
        return LibSystem.sysctlbyname(name, buf, ref size, nint.Zero, 0) == 0
            ? System.Text.Encoding.UTF8.GetString(buf, 0, (int)size).TrimEnd('\0')
            : string.Empty;
    }

    private static int SysctlInt(string name)
    {
        nuint size = 4;
        var buf = new byte[4];
        return LibSystem.sysctlbyname(name, buf, ref size, nint.Zero, 0) == 0
            ? BitConverter.ToInt32(buf, 0) : 0;
    }

    private static long SysctlLong(string name)
    {
        nuint size = 8;
        var buf = new byte[8];
        return LibSystem.sysctlbyname(name, buf, ref size, nint.Zero, 0) == 0
            ? BitConverter.ToInt64(buf, 0) : 0L;
    }

    // ── Cached static data (read once at startup) ──────────────────────────────
    private readonly string _cpuModel;
    private readonly int    _physicalCores;
    private readonly int    _logicalCores;
    private readonly long   _totalMemBytes;
    private readonly int    _pageSize;

    public MacOSSystemMetricsProvider()
    {
        _cpuModel      = SysctlString("machdep.cpu.brand_string");
        _physicalCores = SysctlInt("hw.physicalcpu");
        _logicalCores  = Environment.ProcessorCount;
        _totalMemBytes = SysctlLong("hw.memsize");
        _pageSize      = SysctlInt("hw.pagesize");

        if (_pageSize <= 0)
            _pageSize = 4096; // fallback
    }

    // ── ISystemMetricsProvider ─────────────────────────────────────────────────
    public IObservable<SystemMetrics> GetMetricsStream(TimeSpan interval) =>
        Observable.Timer(TimeSpan.Zero, interval)
                  .Select(_ => BuildMetrics());

    public Task<SystemMetrics> GetMetricsAsync(CancellationToken ct = default) =>
        Task.FromResult(BuildMetrics());

    // ── Snapshot ───────────────────────────────────────────────────────────────
    private SystemMetrics BuildMetrics()
    {
        // Available memory: free pages × page size
        var freePages     = (long)SysctlInt("vm.page_free_count");
        var availableBytes = freePages * _pageSize;
        var usedBytes      = _totalMemBytes > 0
            ? Math.Max(0L, _totalMemBytes - availableBytes)
            : 0L;

        return new SystemMetrics
        {
            Cpu = new CpuMetrics
            {
                TotalPercent  = 0,          // Mach host_statistics — deferred
                CorePercents  = [],
                FrequencyMhz  = 0,
                TemperatureCelsius = 0,
                LogicalCores  = _logicalCores,
                PhysicalCores = _physicalCores,
                ModelName     = _cpuModel,
            },
            Memory = new MemoryMetrics
            {
                TotalBytes     = _totalMemBytes,
                AvailableBytes = availableBytes,
                UsedBytes      = usedBytes,
                CachedBytes         = 0,
                PagedPoolBytes      = 0,
                NonPagedPoolBytes   = 0,
                CommitTotalBytes    = 0,
                CommitLimitBytes    = 0,
            },
            Disks           = [],   // DriveInfo enumeration — deferred
            NetworkAdapters = [],   // NetworkInterface enumeration — deferred
            Gpus            = [],   // Metal/IOKit — deferred
            Timestamp       = DateTime.UtcNow,
        };
    }
}
