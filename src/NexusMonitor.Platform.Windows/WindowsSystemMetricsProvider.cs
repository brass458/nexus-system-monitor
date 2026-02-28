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

    // CPU — one-shot cached detail
    private double _cpuBaseSpeedMhz;
    private int    _cpuSockets = 1;
    private string _cpuVirtualization = string.Empty;
    private long   _cpuL1CacheBytes;
    private long   _cpuL2CacheBytes;
    private long   _cpuL3CacheBytes;

    // Disk (per-disk counters)
    private (string instance, PerformanceCounter read, PerformanceCounter write, PerformanceCounter active,
             PerformanceCounter? avgResponse)[] _diskCounters = [];

    // Disk — one-shot cached detail (keyed by disk index)
    private Dictionary<int, string> _diskTypes = new();     // index → "SSD", "HDD", "NVMe"

    // Network (all active adapters)
    private (string instance, PerformanceCounter send, PerformanceCounter recv)[] _netCounters = [];

    // Memory — one-shot cached detail
    private int    _memSpeedMhz;
    private int    _memSlotsUsed;
    private int    _memTotalSlots;
    private string _memFormFactor = string.Empty;
    private long   _memWmiTotalBytes;       // WMI total physical (before hardware reservation)

    // GPU
    private PerformanceCounter[] _gpuEngineCounters = [];
    private PerformanceCounter[]? _gpuMemCounters;
    private PerformanceCounter[] _gpuCopyCounters   = [];
    private PerformanceCounter[] _gpuDecodeCounters  = [];
    private PerformanceCounter[] _gpuEncodeCounters  = [];
    private PerformanceCounter[] _gpuSharedCounters  = [];
    private string _gpuName       = string.Empty;
    private long   _gpuTotalVram  = 0;

    // GPU — one-shot cached detail
    private long   _gpuSharedTotalBytes;
    private string _gpuDriverVersion   = string.Empty;
    private string _gpuPhysicalLocation = string.Empty;

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

        // System-wide process/thread/handle counts from PERFORMANCE_INFORMATION
        var pi = new PERFORMANCE_INFORMATION { cb = (uint)Marshal.SizeOf<PERFORMANCE_INFORMATION>() };
        PsApi.GetPerformanceInfo(ref pi, pi.cb);

        return new CpuMetrics
        {
            TotalPercent          = Math.Round(total, 1),
            CorePercents          = corePercents,
            FrequencyMhz          = SampleCpuFrequencyMhz(),
            TemperatureCelsius    = SampleCpuTemperatureC(),
            LogicalCores          = _logicalCores,
            PhysicalCores         = _physicalCores,
            ModelName             = _cpuModel,
            BaseSpeedMhz          = _cpuBaseSpeedMhz,
            Sockets               = _cpuSockets,
            VirtualizationStatus  = _cpuVirtualization,
            L1CacheBytes          = _cpuL1CacheBytes,
            L2CacheBytes          = _cpuL2CacheBytes,
            L3CacheBytes          = _cpuL3CacheBytes,
            ProcessCount          = (int)pi.ProcessCount,
            ThreadCount           = (int)pi.ThreadCount,
            HandleCount           = (int)pi.HandleCount,
        };
    }

    private MemoryMetrics SampleMemory()
    {
        var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        Kernel32.GlobalMemoryStatusEx(ref ms);

        var pi = new PERFORMANCE_INFORMATION { cb = (uint)Marshal.SizeOf<PERFORMANCE_INFORMATION>() };
        PsApi.GetPerformanceInfo(ref pi, pi.cb);

        long pageSize = (long)pi.PageSize;
        long totalBytes = (long)ms.ullTotalPhys;

        // Hardware reserved = WMI total (installed RAM) minus OS-visible RAM
        long hwReserved = _memWmiTotalBytes > totalBytes ? _memWmiTotalBytes - totalBytes : 0;

        return new MemoryMetrics
        {
            TotalBytes            = totalBytes,
            UsedBytes             = (long)(ms.ullTotalPhys - ms.ullAvailPhys),
            AvailableBytes        = (long)ms.ullAvailPhys,
            CachedBytes           = (long)pi.SystemCache * pageSize,
            PagedPoolBytes        = (long)pi.KernelPaged   * pageSize,
            NonPagedPoolBytes     = (long)pi.KernelNonpaged* pageSize,
            CommitTotalBytes      = (long)pi.CommitTotal   * pageSize,
            CommitLimitBytes      = (long)pi.CommitLimit   * pageSize,
            SpeedMhz              = _memSpeedMhz,
            SlotsUsed             = _memSlotsUsed,
            TotalSlots            = _memTotalSlots,
            FormFactor            = _memFormFactor,
            HardwareReservedBytes = hwReserved,
        };
    }

    private List<DiskMetrics> SampleDisks()
    {
        var drives = DriveInfo.GetDrives()
                              .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                              .ToList();
        var list = new List<DiskMetrics>();

        if (_diskCounters.Length == 0)
        {
            foreach (var d in drives)
                list.Add(new DiskMetrics
                {
                    DriveLetter = d.Name.TrimEnd('\\', '/'),
                    Label       = d.VolumeLabel,
                    TotalBytes  = d.TotalSize,
                    FreeBytes   = d.TotalFreeSpace,
                });
            return list.Count > 0 ? list : [new DiskMetrics { DriveLetter = "C:" }];
        }

        string systemDrive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\', '/').ToUpperInvariant();

        foreach (var (inst, readCtr, writeCtr, activeCtr, avgRespCtr) in _diskCounters)
        {
            double readBytes = 0, writeBytes = 0, activePercent = 0, avgRespSec = 0;
            try { readBytes    = readCtr.NextValue();   } catch { }
            try { writeBytes   = writeCtr.NextValue();  } catch { }
            try { activePercent= activeCtr.NextValue(); } catch { }
            try { if (avgRespCtr != null) avgRespSec = avgRespCtr.NextValue(); } catch { }

            var (idx, letters) = ParseDiskInstance(inst);
            string allLetters  = string.Join(" ", letters.Select(l => l + ":"));
            string firstLetter = letters.Length > 0 ? letters[0] + ":" : string.Empty;

            // Match drives belonging to this physical disk
            var matchedDrives = drives.Where(d =>
                letters.Any(l => d.Name.StartsWith(l, StringComparison.OrdinalIgnoreCase))).ToList();
            var drive = matchedDrives.FirstOrDefault();

            // IsSystemDisk: does this physical disk contain the system drive?
            bool isSystem = letters.Any(l =>
                systemDrive.StartsWith(l, StringComparison.OrdinalIgnoreCase));

            // HasPageFile: check if any volume on this disk has pagefile.sys
            bool hasPageFile = matchedDrives.Any(d =>
            {
                try { return File.Exists(Path.Combine(d.Name, "pagefile.sys")); }
                catch { return false; }
            });

            // Build volume list
            var volumes = matchedDrives.Select(d => new VolumeInfo
            {
                DriveLetter = d.Name.TrimEnd('\\', '/'),
                Label       = d.VolumeLabel,
                FileSystem  = d.DriveFormat,
                TotalBytes  = d.TotalSize,
                FreeBytes   = d.TotalFreeSpace,
            }).ToList();

            // Disk type from cached WMI
            _diskTypes.TryGetValue(idx, out string? diskType);

            list.Add(new DiskMetrics
            {
                DriveLetter      = firstLetter,
                Label            = drive?.VolumeLabel ?? string.Empty,
                DiskIndex        = idx,
                PhysicalName     = $"Physical Drive {idx}",
                AllDriveLetters  = allLetters,
                ReadBytesPerSec  = (long)readBytes,
                WriteBytesPerSec = (long)writeBytes,
                ActivePercent    = activePercent,
                TotalBytes       = drive?.TotalSize ?? 0,
                FreeBytes        = drive?.TotalFreeSpace ?? 0,
                DiskType         = diskType ?? string.Empty,
                AverageResponseMs= Math.Round(avgRespSec * 1000.0, 2),
                IsSystemDisk     = isSystem,
                HasPageFile      = hasPageFile,
                Volumes          = volumes,
            });
        }

        return list.Count > 0 ? list : drives.Select(d => new DiskMetrics
        {
            DriveLetter = d.Name.TrimEnd('\\', '/'),
            Label       = d.VolumeLabel,
            TotalBytes  = d.TotalSize,
            FreeBytes   = d.TotalFreeSpace,
        }).ToList();
    }

    private static (int index, string[] letters) ParseDiskInstance(string name)
    {
        // PDH instance names look like "0 C: D:" or "1 E:"
        var parts = name.Split(' ');
        int.TryParse(parts[0], out int idx);
        var letters = parts.Skip(1)
            .Where(p => p.Length == 2 && p[1] == ':')
            .Select(p => p[0].ToString().ToUpper())
            .ToArray();
        return (idx, letters);
    }

    private List<NetworkAdapterMetrics> SampleNetwork()
    {
        if (_netCounters.Length == 0) return [];

        var nics = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
        var list  = new List<NetworkAdapterMetrics>();

        foreach (var (inst, sendCtr, recvCtr) in _netCounters)
        {
            double send = 0, recv = 0;
            try { send = sendCtr.NextValue(); } catch { }
            try { recv = recvCtr.NextValue(); } catch { }

            // Fuzzy-match PDH instance name to a NetworkInterface (PDH replaces / with _)
            var nic = nics.FirstOrDefault(n =>
            {
                var norm = n.Description.Replace("/", "_").Replace("(", "[").Replace(")", "]");
                return inst.Equals(norm, StringComparison.OrdinalIgnoreCase)
                    || (norm.Length >= 20 && inst.Contains(norm[..20], StringComparison.OrdinalIgnoreCase));
            });

            string ipv4 = string.Empty, ipv6 = string.Empty, type = string.Empty;
            string dnsSuffix = string.Empty, connType = string.Empty;
            long speed = 0;
            if (nic != null)
            {
                try
                {
                    var ip = nic.GetIPProperties();
                    ipv4 = ip.UnicastAddresses
                        .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        .Select(a => a.Address.ToString()).FirstOrDefault() ?? string.Empty;
                    ipv6 = ip.UnicastAddresses
                        .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                        .Select(a => a.Address.ToString()).FirstOrDefault() ?? string.Empty;
                    speed = nic.Speed;
                    dnsSuffix = ip.DnsSuffix ?? string.Empty;
                    bool isWifi = nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211;
                    type     = isWifi ? "IEEE 802.11" : "Ethernet";
                    connType = isWifi ? "Wi-Fi" : nic.NetworkInterfaceType switch
                    {
                        System.Net.NetworkInformation.NetworkInterfaceType.Ethernet  => "Ethernet",
                        System.Net.NetworkInformation.NetworkInterfaceType.Ppp       => "Cellular",
                        System.Net.NetworkInformation.NetworkInterfaceType.Wwanpp    => "Cellular",
                        System.Net.NetworkInformation.NetworkInterfaceType.Wwanpp2   => "Cellular",
                        _ => nic.NetworkInterfaceType.ToString()
                    };
                }
                catch { }
            }

            list.Add(new NetworkAdapterMetrics
            {
                Name            = nic?.Name ?? inst,
                Description     = nic?.Description ?? inst,
                SendBytesPerSec = (long)send,
                RecvBytesPerSec = (long)recv,
                IsConnected     = nic?.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up,
                IPv4Address     = ipv4,
                IPv6Address     = ipv6,
                LinkSpeedBps    = speed,
                AdapterType     = type,
                DnsSuffix       = dnsSuffix,
                ConnectionType  = connType,
            });
        }

        return list;
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
        // ── CPU model + base speed from registry ────────────────────────────────
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            _cpuModel = ((key?.GetValue("ProcessorNameString") as string) ?? "Unknown CPU").Trim();
            _cpuBaseSpeedMhz = Convert.ToDouble(key?.GetValue("~MHz") ?? 0);
        }
        catch { _cpuModel = "Unknown CPU"; }

        _logicalCores  = Environment.ProcessorCount;
        _physicalCores = GetPhysicalCoreCount();

        // ── CPU one-shot WMI (sockets, virtualization, cache) ────────────────
        InitCpuWmiCache();

        // ── Memory one-shot WMI (speed, slots, form factor) ─────────────────
        InitMemoryWmiCache();

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

        // ── Per-disk counters ─────────────────────────────────────────────────
        try
        {
            var diskInstances = new PerformanceCounterCategory("PhysicalDisk")
                .GetInstanceNames()
                .Where(n => n != "_Total")
                .OrderBy(n => n)
                .ToArray();
            var diskList = new List<(string, PerformanceCounter, PerformanceCounter, PerformanceCounter, PerformanceCounter?)>();
            foreach (var inst in diskInstances)
            {
                try
                {
                    var r = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec",  inst, readOnly: true);
                    var w = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", inst, readOnly: true);
                    var a = new PerformanceCounter("PhysicalDisk", "% Disk Time",          inst, readOnly: true);
                    PerformanceCounter? ar = null;
                    try
                    {
                        ar = new PerformanceCounter("PhysicalDisk", "Avg. Disk sec/Transfer", inst, readOnly: true);
                        ar.NextValue();
                    }
                    catch { ar = null; }
                    r.NextValue(); w.NextValue(); a.NextValue();
                    diskList.Add((inst, r, w, a, ar));
                }
                catch { }
            }
            _diskCounters = diskList.ToArray();
        }
        catch { _diskCounters = []; }

        // ── Disk one-shot WMI (media type) ──────────────────────────────────
        InitDiskWmiCache();

        // ── Network counters (all active adapters) ────────────────────────────
        try
        {
            var netInstances = new PerformanceCounterCategory("Network Interface")
                .GetInstanceNames()
                .Where(n => !n.Contains("Loopback",        StringComparison.OrdinalIgnoreCase)
                         && !n.Contains("Pseudo-Interface", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var netList = new List<(string, PerformanceCounter, PerformanceCounter)>();
            foreach (var inst in netInstances)
            {
                try
                {
                    var s = new PerformanceCounter("Network Interface", "Bytes Sent/sec",     inst, readOnly: true);
                    var r = new PerformanceCounter("Network Interface", "Bytes Received/sec", inst, readOnly: true);
                    s.NextValue(); r.NextValue();
                    netList.Add((inst, s, r));
                }
                catch { }
            }
            _netCounters = netList.ToArray();
        }
        catch { _netCounters = []; }

        // ── GPU ───────────────────────────────────────────────────────────────
        InitGpu();
    }

    private void InitGpu()
    {
        // 1. WMI: name + total VRAM + driver + location (runs once; slow but acceptable at startup)
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterRAM, DriverVersion, PNPDeviceID FROM Win32_VideoController WHERE VideoProcessor <> NULL");
            using var results = searcher.Get();
            foreach (ManagementBaseObject obj in results)
            using (obj)
            {
                _gpuName            = (obj["Name"]          as string ?? string.Empty).Trim();
                _gpuTotalVram       = Convert.ToInt64(obj["AdapterRAM"] ?? 0L);
                _gpuDriverVersion   = (obj["DriverVersion"] as string ?? string.Empty).Trim();
                _gpuPhysicalLocation= (obj["PNPDeviceID"]   as string ?? string.Empty).Trim();
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

        _gpuCopyCounters   = InitEngineCounters("engtype_Copy");
        _gpuDecodeCounters = InitEngineCounters("engtype_VideoDecode");
        _gpuEncodeCounters = InitEngineCounters("engtype_VideoEncode");

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

        // GPU shared memory usage + total
        try
        {
            var memCat    = new PerformanceCounterCategory("GPU Adapter Memory");
            var memInsts  = memCat.GetInstanceNames();
            _gpuSharedCounters = memInsts
                .Select(inst =>
                {
                    var c = new PerformanceCounter("GPU Adapter Memory", "Shared Usage", inst, readOnly: true);
                    try { c.NextValue(); } catch { }
                    return c;
                })
                .ToArray();
            // Shared total (read once from "Shared Limit" counter)
            foreach (var inst in memInsts)
            {
                try
                {
                    using var lim = new PerformanceCounter("GPU Adapter Memory", "Shared Limit", inst, readOnly: true);
                    _gpuSharedTotalBytes += (long)lim.NextValue();
                }
                catch { }
            }
        }
        catch { _gpuSharedCounters = []; }
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

    // ─── One-shot WMI cache helpers ────────────────────────────────────────────

    private void InitCpuWmiCache()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT NumberOfCores, L2CacheSize, L3CacheSize, VirtualizationFirmwareEnabled, SocketDesignation " +
                "FROM Win32_Processor");
            using var results = searcher.Get();
            var sockets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ManagementBaseObject obj in results)
            using (obj)
            {
                var sock = obj["SocketDesignation"] as string ?? "Socket";
                sockets.Add(sock);

                // L2/L3 are in KB in WMI
                _cpuL2CacheBytes = Convert.ToInt64(obj["L2CacheSize"] ?? 0) * 1024L;
                _cpuL3CacheBytes = Convert.ToInt64(obj["L3CacheSize"] ?? 0) * 1024L;

                try
                {
                    bool virtEnabled = Convert.ToBoolean(obj["VirtualizationFirmwareEnabled"] ?? false);
                    _cpuVirtualization = virtEnabled ? "Enabled" : "Disabled";
                }
                catch { _cpuVirtualization = "Not available"; }
            }
            _cpuSockets = Math.Max(1, sockets.Count);
        }
        catch { /* WMI unavailable — keep defaults */ }

        // L1 cache: use GetLogicalProcessorInformation or estimate
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT MaxCacheSize, Level FROM Win32_CacheMemory WHERE Level = 3");
            using var results = searcher.Get();
            foreach (ManagementBaseObject obj in results)
            using (obj)
            {
                // Level 3 in WMI = L1 data cache (WMI Level encoding: 3=L1, 4=L2, 5=L3)
                _cpuL1CacheBytes = Convert.ToInt64(obj["MaxCacheSize"] ?? 0) * 1024L;
                break;
            }
        }
        catch { /* L1 info unavailable */ }
    }

    private void InitMemoryWmiCache()
    {
        // Physical memory sticks: speed + form factor + count
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Speed, FormFactor, Capacity FROM Win32_PhysicalMemory");
            using var results = searcher.Get();
            int slotsUsed = 0;
            long wmiTotal = 0;
            foreach (ManagementBaseObject obj in results)
            using (obj)
            {
                slotsUsed++;
                if (_memSpeedMhz == 0)
                    _memSpeedMhz = Convert.ToInt32(obj["Speed"] ?? 0);
                if (string.IsNullOrEmpty(_memFormFactor))
                    _memFormFactor = MapMemoryFormFactor(Convert.ToInt32(obj["FormFactor"] ?? 0));
                wmiTotal += Convert.ToInt64(obj["Capacity"] ?? 0);
            }
            _memSlotsUsed = slotsUsed;
            _memWmiTotalBytes = wmiTotal;
        }
        catch { /* WMI unavailable */ }

        // Total physical slots
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT MemoryDevices FROM Win32_PhysicalMemoryArray");
            using var results = searcher.Get();
            foreach (ManagementBaseObject obj in results)
            using (obj)
            {
                _memTotalSlots += Convert.ToInt32(obj["MemoryDevices"] ?? 0);
            }
        }
        catch { /* fallback: _memTotalSlots stays 0 */ }
    }

    private void InitDiskWmiCache()
    {
        try
        {
            // Try MSFT_PhysicalDisk (Storage cmdlet namespace) for best media type info
            using var searcher = new ManagementObjectSearcher(
                @"\\.\root\Microsoft\Windows\Storage",
                "SELECT DeviceId, MediaType FROM MSFT_PhysicalDisk");
            using var results = searcher.Get();
            foreach (ManagementBaseObject obj in results)
            using (obj)
            {
                var devId = (obj["DeviceId"] as string ?? string.Empty).Trim();
                if (int.TryParse(devId, out int idx))
                {
                    int mediaType = Convert.ToInt32(obj["MediaType"] ?? 0);
                    _diskTypes[idx] = mediaType switch
                    {
                        3 => "HDD",
                        4 => "SSD",
                        5 => "SCM", // Storage Class Memory
                        _ => "Unknown"
                    };
                }
            }
        }
        catch
        {
            // Fallback: try Win32_DiskDrive
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Index, MediaType, Model FROM Win32_DiskDrive");
                using var results = searcher.Get();
                foreach (ManagementBaseObject obj in results)
                using (obj)
                {
                    int idx = Convert.ToInt32(obj["Index"] ?? -1);
                    var mt  = (obj["MediaType"] as string ?? string.Empty).ToLowerInvariant();
                    var mdl = (obj["Model"]     as string ?? string.Empty).ToLowerInvariant();
                    string type = "Unknown";
                    if (mt.Contains("ssd") || mdl.Contains("nvme") || mdl.Contains("ssd"))
                        type = mdl.Contains("nvme") ? "NVMe" : "SSD";
                    else if (mt.Contains("fixed") || mt.Contains("hdd"))
                        type = "HDD";
                    if (idx >= 0) _diskTypes[idx] = type;
                }
            }
            catch { /* no disk type info available */ }
        }
    }

    private static string MapMemoryFormFactor(int code) => code switch
    {
        8  => "DIMM",
        12 => "SODIMM",
        9  => "SIMM",
        13 => "RIMM",
        15 => "FB-DIMM",
        _  => code == 0 ? "Unknown" : $"Type {code}"
    };

    private static PerformanceCounter[] InitEngineCounters(string engineType)
    {
        try
        {
            var cat      = new PerformanceCounterCategory("GPU Engine");
            var counters = new List<PerformanceCounter>();
            foreach (var inst in cat.GetInstanceNames())
            {
                if (!inst.Contains(engineType, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var c = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst, readOnly: true);
                    c.NextValue();
                    counters.Add(c);
                }
                catch { }
            }
            return counters.ToArray();
        }
        catch { return []; }
    }

    /// <summary>
    /// Sums GPU engine utilization counters and caps at 100%.
    /// Returns an empty list if no GPU was detected at init time.
    /// </summary>
    private IReadOnlyList<GpuMetrics> SampleGpu()
    {
        if (string.IsNullOrEmpty(_gpuName)) return [];

        double usage3D = 0, usageCopy = 0, usageDecode = 0, usageEncode = 0;
        foreach (var c in _gpuEngineCounters)  try { usage3D     += c.NextValue(); } catch { }
        foreach (var c in _gpuCopyCounters)    try { usageCopy   += c.NextValue(); } catch { }
        foreach (var c in _gpuDecodeCounters)  try { usageDecode += c.NextValue(); } catch { }
        foreach (var c in _gpuEncodeCounters)  try { usageEncode += c.NextValue(); } catch { }
        usage3D     = Math.Clamp(usage3D,     0, 100);
        usageCopy   = Math.Clamp(usageCopy,   0, 100);
        usageDecode = Math.Clamp(usageDecode, 0, 100);
        usageEncode = Math.Clamp(usageEncode, 0, 100);

        long dedicatedUsed = _gpuMemCounters is { Length: > 0 }
            ? (long)_gpuMemCounters.Sum(c => { try { return (double)c.NextValue(); } catch { return 0d; } })
            : 0;
        long sharedUsed = _gpuSharedCounters is { Length: > 0 }
            ? (long)_gpuSharedCounters.Sum(c => { try { return (double)c.NextValue(); } catch { return 0d; } })
            : 0;

        return
        [
            new GpuMetrics
            {
                Name                      = _gpuName,
                UsagePercent              = Math.Round(usage3D, 1),
                DedicatedMemoryTotalBytes = _gpuTotalVram,
                DedicatedMemoryUsedBytes  = dedicatedUsed,
                SharedMemoryUsedBytes     = sharedUsed,
                SharedMemoryTotalBytes    = _gpuSharedTotalBytes,
                TemperatureCelsius        = 0,
                Engine3DPercent           = Math.Round(usage3D,     1),
                EngineCopyPercent         = Math.Round(usageCopy,   1),
                EngineVideoDecodePercent  = Math.Round(usageDecode, 1),
                EngineVideoEncodePercent  = Math.Round(usageEncode, 1),
                DriverVersion             = _gpuDriverVersion,
                DirectXVersion            = "DirectX 12",
                PhysicalLocation          = _gpuPhysicalLocation,
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
        foreach (var (_, r, w, a, ar) in _diskCounters) { r?.Dispose(); w?.Dispose(); a?.Dispose(); ar?.Dispose(); }
        foreach (var (_, s, r) in _netCounters) { s?.Dispose(); r?.Dispose(); }
        foreach (var c in _gpuEngineCounters) c?.Dispose();
        if (_gpuMemCounters != null) foreach (var c in _gpuMemCounters) c?.Dispose();
        foreach (var c in _gpuCopyCounters)   c?.Dispose();
        foreach (var c in _gpuDecodeCounters)  c?.Dispose();
        foreach (var c in _gpuEncodeCounters)  c?.Dispose();
        foreach (var c in _gpuSharedCounters)  c?.Dispose();
    }
}
