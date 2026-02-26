using System.Diagnostics;
using System.Management;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;
using NexusMonitor.Platform.Windows.Native;

namespace NexusMonitor.Platform.Windows;

/// <summary>
/// Real Windows system-metrics provider using PerformanceCounters, GlobalMemoryStatusEx
/// and the Registry.  Counters are initialised lazily on first call and "warm-up"
/// discarded (PerformanceCounter rate counters always return 0 on the first read).
/// </summary>
public sealed class WindowsSystemMetricsProvider : ISystemMetricsProvider, IDisposable
{
    // ─── Lazy-init state ──────────────────────────────────────────────────────

    private volatile bool _initialized;
    private readonly object _lock = new();

    // CPU
    private PerformanceCounter?   _cpuTotal = null;
    private PerformanceCounter[]  _cpuCores = [];
    private string _cpuModel    = string.Empty;
    private int    _logicalCores;
    private int    _physicalCores;

    // Disk (aggregate _Total)
    private PerformanceCounter? _diskRead  = null;
    private PerformanceCounter? _diskWrite = null;
    private PerformanceCounter? _diskActive= null;
    private string[] _diskInstances = [];

    // Network (first active adapter)
    private PerformanceCounter? _netSend   = null;
    private PerformanceCounter? _netRecv   = null;
    private string _netAdapterName = string.Empty;

    // GPU
    private PerformanceCounter[] _gpuEngineCounters = [];
    private PerformanceCounter[]? _gpuMemCounters;
    private string _gpuName       = string.Empty;
    private long   _gpuTotalVram  = 0;

    // ─── ISystemMetricsProvider ───────────────────────────────────────────────

    public IObservable<SystemMetrics> GetMetricsStream(TimeSpan interval) =>
        Observable.Timer(TimeSpan.Zero, interval)
                  .Select(_ => Sample());

    public Task<SystemMetrics> GetMetricsAsync(CancellationToken ct = default) =>
        Task.Run(Sample, ct);

    // ─── Sampling ─────────────────────────────────────────────────────────────

    private SystemMetrics Sample()
    {
        EnsureInitialized();
        return new SystemMetrics
        {
            Cpu            = SampleCpu(),
            Memory         = SampleMemory(),
            Disks          = SampleDisks(),
            NetworkAdapters= SampleNetwork(),
            Gpus           = SampleGpu(),
            Timestamp      = DateTime.UtcNow,
        };
    }

    private CpuMetrics SampleCpu()
    {
        double total = 0;
        try { total = _cpuTotal?.NextValue() ?? 0; } catch { }

        var corePercents = new double[_cpuCores.Length];
        for (int i = 0; i < _cpuCores.Length; i++)
            try { corePercents[i] = _cpuCores[i]?.NextValue() ?? 0; } catch { }

        return new CpuMetrics
        {
            TotalPercent       = Math.Round(total, 1),
            CorePercents       = corePercents,
            FrequencyMhz       = SampleCpuFrequencyMhz(),
            TemperatureCelsius = SampleCpuTemperatureC(),
            LogicalCores       = _logicalCores,
            PhysicalCores      = _physicalCores,
            ModelName          = _cpuModel,
        };
    }

    private static MemoryMetrics SampleMemory()
    {
        var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        Kernel32.GlobalMemoryStatusEx(ref ms);

        var pi = new PERFORMANCE_INFORMATION { cb = (uint)Marshal.SizeOf<PERFORMANCE_INFORMATION>() };
        PsApi.GetPerformanceInfo(ref pi, pi.cb);

        long pageSize = (long)pi.PageSize;

        return new MemoryMetrics
        {
            TotalBytes       = (long)ms.ullTotalPhys,
            UsedBytes        = (long)(ms.ullTotalPhys - ms.ullAvailPhys),
            AvailableBytes   = (long)ms.ullAvailPhys,
            CachedBytes      = (long)pi.SystemCache * pageSize,
            PagedPoolBytes   = (long)pi.KernelPaged   * pageSize,
            NonPagedPoolBytes= (long)pi.KernelNonpaged* pageSize,
            CommitTotalBytes = (long)pi.CommitTotal   * pageSize,
            CommitLimitBytes = (long)pi.CommitLimit   * pageSize,
        };
    }

    private List<DiskMetrics> SampleDisks()
    {
        double readBytes = 0, writeBytes = 0, activePercent = 0;
        try { readBytes    = _diskRead?.NextValue()   ?? 0; } catch { }
        try { writeBytes   = _diskWrite?.NextValue()  ?? 0; } catch { }
        try { activePercent= _diskActive?.NextValue() ?? 0; } catch { }

        // Enumerate fixed drives for space info
        var drives = DriveInfo.GetDrives()
                              .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                              .ToList();

        // First drive gets the aggregate throughput; others space-only
        var list = new List<DiskMetrics>();
        for (int i = 0; i < drives.Count; i++)
        {
            var d = drives[i];
            list.Add(new DiskMetrics
            {
                DriveLetter      = d.Name.TrimEnd('\\', '/'),
                Label            = d.VolumeLabel,
                ReadBytesPerSec  = i == 0 ? (long)readBytes  : 0,
                WriteBytesPerSec = i == 0 ? (long)writeBytes : 0,
                ActivePercent    = i == 0 ? activePercent    : 0,
                TotalBytes       = d.TotalSize,
                FreeBytes        = d.TotalFreeSpace,
            });
        }

        if (list.Count == 0)
        {
            list.Add(new DiskMetrics
            {
                DriveLetter      = "C:",
                ReadBytesPerSec  = (long)readBytes,
                WriteBytesPerSec = (long)writeBytes,
                ActivePercent    = activePercent,
            });
        }

        return list;
    }

    private List<NetworkAdapterMetrics> SampleNetwork()
    {
        double send = 0, recv = 0;
        try { send = _netSend?.NextValue() ?? 0; } catch { }
        try { recv = _netRecv?.NextValue() ?? 0; } catch { }

        if (string.IsNullOrEmpty(_netAdapterName)) return [];

        return
        [
            new NetworkAdapterMetrics
            {
                Name             = _netAdapterName,
                Description      = _netAdapterName,
                SendBytesPerSec  = (long)send,
                RecvBytesPerSec  = (long)recv,
                IsConnected      = true,
            }
        ];
    }

    // ─── Lazy initialisation ──────────────────────────────────────────────────

    private void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_lock)
        {
            if (_initialized) return;
            InitCounters();
            _initialized = true;
        }
    }

    private void InitCounters()
    {
        // ── CPU model from registry ────────────────────────────────────────────
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            _cpuModel = ((key?.GetValue("ProcessorNameString") as string) ?? "Unknown CPU").Trim();
        }
        catch { _cpuModel = "Unknown CPU"; }

        _logicalCores  = Environment.ProcessorCount;
        _physicalCores = GetPhysicalCoreCount();

        // ── CPU total counter ─────────────────────────────────────────────────
        try
        {
            _cpuTotal = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);
            _cpuTotal.NextValue(); // warm-up (always returns 0 on first call)
        }
        catch { _cpuTotal = null; }

        // ── Per-core counters ─────────────────────────────────────────────────
        try
        {
            var coreList = new List<PerformanceCounter>();
            for (int i = 0; i < _logicalCores; i++)
            {
                try
                {
                    var c = new PerformanceCounter("Processor", "% Processor Time", i.ToString(), readOnly: true);
                    c.NextValue(); // warm-up
                    coreList.Add(c);
                }
                catch { coreList.Add(null!); }
            }
            _cpuCores = coreList.ToArray();
        }
        catch { _cpuCores = []; }

        // ── Disk counters (_Total for aggregate throughput) ───────────────────
        try
        {
            _diskRead   = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec",  "_Total", readOnly: true);
            _diskWrite  = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total", readOnly: true);
            _diskActive = new PerformanceCounter("PhysicalDisk", "% Disk Time",          "_Total", readOnly: true);
            _diskRead.NextValue();
            _diskWrite.NextValue();
            _diskActive.NextValue();
        }
        catch { _diskRead = _diskWrite = _diskActive = null; }

        // ── Network counters (first non-loopback adapter) ─────────────────────
        try
        {
            string[] instances = new PerformanceCounterCategory("Network Interface").GetInstanceNames();
            // Prefer adapters that aren't loopback / virtual; fall back to first available
            string? chosen = instances.FirstOrDefault(n =>
                !n.Contains("Loopback", StringComparison.OrdinalIgnoreCase)
             && !n.Contains("Pseudo-Interface", StringComparison.OrdinalIgnoreCase))
                ?? instances.FirstOrDefault();

            if (chosen != null)
            {
                _netAdapterName = chosen;
                _netSend = new PerformanceCounter("Network Interface", "Bytes Sent/sec",     chosen, readOnly: true);
                _netRecv = new PerformanceCounter("Network Interface", "Bytes Received/sec", chosen, readOnly: true);
                _netSend.NextValue();
                _netRecv.NextValue();
            }
        }
        catch { _netSend = _netRecv = null; }

        // ── GPU ───────────────────────────────────────────────────────────────
        InitGpu();
    }

    private void InitGpu()
    {
        // 1. WMI: name + total VRAM (runs once; slow but acceptable at startup)
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterRAM FROM Win32_VideoController WHERE VideoProcessor <> NULL");
            using var results = searcher.Get();
            foreach (ManagementBaseObject obj in results)
            using (obj)
            {
                _gpuName     = (obj["Name"]       as string ?? string.Empty).Trim();
                _gpuTotalVram = System.Convert.ToInt64(obj["AdapterRAM"] ?? 0L);
                break; // first GPU
            }
        }
        catch { _gpuName = string.Empty; }

        // 2. PDH: GPU Engine utilization (Windows 10 1803+, all GPU vendors)
        try
        {
            var cat       = new PerformanceCounterCategory("GPU Engine");
            var instances = cat.GetInstanceNames();
            // Each instance is like "pid_X_luid_0xY_phys_0_eng_0_engtype_3D"
            // Collect all 3D engine instances across all PIDs for total utilization
            var counters = new List<PerformanceCounter>();
            foreach (var inst in instances)
            {
                if (!inst.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var c = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst, readOnly: true);
                    c.NextValue(); // warm-up
                    counters.Add(c);
                }
                catch { }
            }
            _gpuEngineCounters = counters.ToArray();
        }
        catch { _gpuEngineCounters = []; }

        // GPU dedicated memory usage (Windows 10+, works on NVIDIA/AMD/Intel)
        try
        {
            var memCat = new PerformanceCounterCategory("GPU Adapter Memory");
            _gpuMemCounters = memCat.GetInstanceNames()
                .Select(inst => new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", inst, readOnly: true))
                .ToArray();
            // Warm up — first read always returns 0
            foreach (var c in _gpuMemCounters) c.NextValue();
        }
        catch { _gpuMemCounters = null; }
    }

    /// <summary>
    /// Reads current CPU clock speed from HKLM\…\CentralProcessor\0\~MHz.
    /// Windows updates this value every second; much faster than WMI.
    /// </summary>
    private static double SampleCpuFrequencyMhz()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return System.Convert.ToDouble(key?.GetValue("~MHz") ?? 0);
        }
        catch { return 0; }
    }

    /// <summary>
    /// Reads CPU temperature from ACPI thermal zones via WMI root\wmi.
    /// Returns the maximum zone temperature in Celsius, or 0 if unavailable.
    /// </summary>
    private static double SampleCpuTemperatureC()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\wmi",
                "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            double max = 0;
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            using (obj)
            {
                // CurrentTemperature is in tenths of Kelvin
                double tempK = Convert.ToDouble(obj["CurrentTemperature"]);
                double tempC = (tempK - 2732.0) / 10.0;
                if (tempC > max) max = tempC;
            }
            return max < 1.0 ? 0 : Math.Round(max, 1);
        }
        catch { return 0; }
    }

    /// <summary>
    /// Sums all GPU 3D-engine utilization counters and caps at 100%.
    /// Returns an empty list if no GPU was detected at init time.
    /// </summary>
    private IReadOnlyList<GpuMetrics> SampleGpu()
    {
        if (string.IsNullOrEmpty(_gpuName)) return [];

        double usage = 0;
        foreach (var c in _gpuEngineCounters)
            try { usage += c.NextValue(); } catch { }
        usage = Math.Clamp(usage, 0, 100);

        return
        [
            new GpuMetrics
            {
                Name                      = _gpuName,
                UsagePercent              = Math.Round(usage, 1),
                DedicatedMemoryTotalBytes = _gpuTotalVram,
                DedicatedMemoryUsedBytes  = _gpuMemCounters is { Length: > 0 }
                    ? (long)_gpuMemCounters.Sum(c => { try { return (double)c.NextValue(); } catch { return 0d; } })
                    : 0,
                TemperatureCelsius        = 0,   // requires NVML / sensor — future phase
            }
        ];
    }

    private static int GetPhysicalCoreCount()
    {
        // Read from registry (fastest, no WMI)
        try
        {
            int count = 0;
            for (int i = 0; ; i++)
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    $@"HARDWARE\DESCRIPTION\System\CentralProcessor\{i}");
                if (key is null) break;
                count++;
            }
            return Math.Max(1, count);
        }
        catch { return Environment.ProcessorCount / 2; }
    }

    // ─── IDisposable ──────────────────────────────────────────────────────────

    public void Dispose()
    {
        _cpuTotal?.Dispose();
        foreach (var c in _cpuCores) c?.Dispose();
        _diskRead?.Dispose();
        _diskWrite?.Dispose();
        _diskActive?.Dispose();
        _netSend?.Dispose();
        _netRecv?.Dispose();
        foreach (var c in _gpuEngineCounters) c?.Dispose();
        if (_gpuMemCounters != null) foreach (var c in _gpuMemCounters) c?.Dispose();
    }
}
