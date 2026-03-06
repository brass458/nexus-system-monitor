using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Platform.Linux;

public sealed class LinuxSystemMetricsProvider : ISystemMetricsProvider
{
    // ── P/Invoke ───────────────────────────────────────────────────────────────
    [DllImport("libc.so.6", SetLastError = true)]
    private static extern int statvfs(string path, out Statvfs buf);

    [StructLayout(LayoutKind.Sequential)]
    private struct Statvfs
    {
        public ulong f_bsize;
        public ulong f_frsize;
        public ulong f_blocks;
        public ulong f_bfree;
        public ulong f_bavail;
        public ulong f_files;
        public ulong f_ffree;
        public ulong f_favail;
        public ulong f_fsid;
        public ulong f_flag;
        public ulong f_namemax;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public int[] Spare;
    }

    // ── Cached static data ─────────────────────────────────────────────────────
    private readonly string _cpuModel;
    private readonly int    _logicalCores;
    private readonly int    _physicalCores;
    private readonly long   _totalMemBytes;

    // ── Delta tracking ─────────────────────────────────────────────────────────
    private long[]   _prevCpuFields   = [];
    private DateTime _prevCpuTime     = DateTime.MinValue;
    // Per-core delta tracking: core index → previous fields array
    private readonly Dictionary<int, long[]> _prevCoreFields = new();

    private readonly Dictionary<string, (long readSectors, long writeSectors)> _prevDisk = new();
    // io_ticks: field 12 (0-indexed) in /proc/diskstats — milliseconds device was active
    private readonly Dictionary<string, long> _prevDiskIoTicks = new();
    private DateTime _prevDiskTime = DateTime.MinValue;

    private readonly Dictionary<string, (long rxBytes, long txBytes)> _prevNet = new();
    private DateTime _prevNetTime = DateTime.MinValue;

    // /proc/mounts cache — mount topology changes rarely; refresh every 30 s
    private Dictionary<string, string?> _mountsCache = new(); // devName → mountPoint
    private DateTime _mountsCacheTime = DateTime.MinValue;
    private static readonly TimeSpan MountsCacheDuration = TimeSpan.FromSeconds(30);

    // NVIDIA detection — checked once at startup
    private static readonly bool _hasNvidiaSmi = File.Exists("/usr/bin/nvidia-smi") || File.Exists("/usr/local/bin/nvidia-smi");

    public LinuxSystemMetricsProvider()
    {
        _cpuModel      = ReadCpuModel();
        _logicalCores  = Environment.ProcessorCount;
        _physicalCores = ReadPhysicalCores();
        _totalMemBytes = ReadTotalMem();
    }

    // Shared multicast observable
    private IObservable<SystemMetrics>? _shared;
    private readonly object _sharedLock = new();

    // ── ISystemMetricsProvider ─────────────────────────────────────────────────
    public IObservable<SystemMetrics> GetMetricsStream(TimeSpan interval)
    {
        lock (_sharedLock)
        {
            if (_shared is null)
            {
                var sharedInterval = interval < TimeSpan.FromSeconds(2)
                    ? TimeSpan.FromSeconds(2) : interval;
                _shared = Observable.Timer(TimeSpan.Zero, sharedInterval)
                                    .Select(_ => BuildMetrics())
                                    .Publish()
                                    .RefCount();
            }
            return _shared;
        }
    }

    public Task<SystemMetrics> GetMetricsAsync(CancellationToken ct = default) =>
        Task.FromResult(BuildMetrics());

    // ── Snapshot ───────────────────────────────────────────────────────────────
    private SystemMetrics BuildMetrics()
    {
        return new SystemMetrics
        {
            Cpu             = ReadCpu(),
            Memory          = ReadMemory(),
            Disks           = ReadDisks(),
            NetworkAdapters = ReadNetwork(),
            Gpus            = ReadGpu(),
            Timestamp       = DateTime.UtcNow,
        };
    }

    // ── CPU (/proc/stat) ───────────────────────────────────────────────────────
    private CpuMetrics ReadCpu()
    {
        double totalPercent = 0;
        var    corePercents = new List<double>();
        double freqMhz      = 0;
        double tempC        = 0;

        try
        {
            var lines  = File.ReadAllLines("/proc/stat");
            var now    = DateTime.UtcNow;

            // First line: cpu user nice system idle iowait irq softirq steal
            var cpuLine = lines.FirstOrDefault(l => l.StartsWith("cpu ", StringComparison.Ordinal));
            if (cpuLine != null)
            {
                var fields = ParseCpuLine(cpuLine);
                if (_prevCpuFields.Length == fields.Length)
                {
                    var prevTotal  = _prevCpuFields.Sum();
                    var curTotal   = fields.Sum();
                    var prevIdle   = _prevCpuFields[3];  // idle
                    var curIdle    = fields[3];

                    var totalDelta = curTotal - prevTotal;
                    var idleDelta  = curIdle  - prevIdle;

                    if (totalDelta > 0)
                        totalPercent = Math.Clamp((double)(totalDelta - idleDelta) / totalDelta * 100.0, 0, 100);
                }
                _prevCpuFields = fields;
                _prevCpuTime   = now;
            }

            // Per-core lines: cpu0 cpu1 ... — use deltas like the total CPU line
            for (int i = 0; ; i++)
            {
                var coreLine = lines.FirstOrDefault(l => l.StartsWith($"cpu{i} ", StringComparison.Ordinal));
                if (coreLine == null) break;
                var fields = ParseCpuLine(coreLine);
                if (fields.Length >= 4)
                {
                    double pct = 0;
                    if (_prevCoreFields.TryGetValue(i, out var prevFields) && prevFields.Length >= 4)
                    {
                        var curTotal  = fields.Sum();
                        var prevTotal = prevFields.Sum();
                        var totalDelta = curTotal - prevTotal;
                        var idleDelta  = fields[3] - prevFields[3];
                        if (totalDelta > 0)
                            pct = Math.Clamp((double)(totalDelta - idleDelta) / totalDelta * 100.0, 0, 100);
                    }
                    _prevCoreFields[i] = fields;
                    corePercents.Add(pct);
                }
            }
        }
        catch { }

        // CPU frequency
        try
        {
            var freqPath = "/sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq";
            if (File.Exists(freqPath))
            {
                var txt = File.ReadAllText(freqPath).Trim();
                if (long.TryParse(txt, out var kHz)) freqMhz = kHz / 1000.0;
            }
        }
        catch { }

        // Temperature — scan hwmon for CPU sensor (coretemp/k10temp/zenpower), fall back to thermal_zone*
        try
        {
            tempC = ReadHwmonTemperature();
        }
        catch { }

        return new CpuMetrics
        {
            TotalPercent       = totalPercent,
            CorePercents       = corePercents,
            FrequencyMhz       = freqMhz,
            TemperatureCelsius = tempC,
            LogicalCores       = _logicalCores,
            PhysicalCores      = _physicalCores,
            ModelName          = _cpuModel,
        };
    }

    private static long[] ParseCpuLine(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // parts[0] = "cpu" or "cpu0" etc., parts[1..] = values
        var values = new List<long>();
        for (int i = 1; i < parts.Length; i++)
        {
            if (long.TryParse(parts[i], out var v)) values.Add(v);
        }
        return [.. values];
    }

    // ── Memory (/proc/meminfo) ─────────────────────────────────────────────────
    private MemoryMetrics ReadMemory()
    {
        long available = 0, free = 0, cached = 0, swapTotal = 0, swapFree = 0;

        try
        {
            foreach (var line in File.ReadAllLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                    available = ParseMeminfoKb(line);
                else if (line.StartsWith("MemFree:", StringComparison.Ordinal))
                    free = ParseMeminfoKb(line);
                else if (line.StartsWith("Cached:", StringComparison.Ordinal))
                    cached = ParseMeminfoKb(line);
                else if (line.StartsWith("SwapTotal:", StringComparison.Ordinal))
                    swapTotal = ParseMeminfoKb(line);
                else if (line.StartsWith("SwapFree:", StringComparison.Ordinal))
                    swapFree = ParseMeminfoKb(line);
            }
        }
        catch { }

        var availableBytes = available > 0 ? available * 1024L : free * 1024L;
        var usedBytes      = _totalMemBytes > 0
            ? Math.Max(0L, _totalMemBytes - availableBytes)
            : 0L;

        return new MemoryMetrics
        {
            TotalBytes          = _totalMemBytes,
            AvailableBytes      = availableBytes,
            UsedBytes           = usedBytes,
            CachedBytes         = cached * 1024L,
            PagedPoolBytes      = (swapTotal - swapFree) * 1024L,
            NonPagedPoolBytes   = 0,
            CommitTotalBytes    = 0,
            CommitLimitBytes    = 0,
        };
    }

    private static long ParseMeminfoKb(string line)
    {
        // "MemAvailable:  123456 kB"
        var parts = line.Split(':', 2);
        if (parts.Length < 2) return 0;
        var val = parts[1].Trim().Split(' ')[0];
        return long.TryParse(val, out var v) ? v : 0;
    }

    // ── Disks (/proc/diskstats) ────────────────────────────────────────────────
    private IReadOnlyList<DiskMetrics> ReadDisks()
    {
        var result  = new List<DiskMetrics>();
        var now     = DateTime.UtcNow;
        var elapsed = _prevDiskTime == DateTime.MinValue
            ? 1.0
            : Math.Max(0.01, (now - _prevDiskTime).TotalSeconds);

        try
        {
            var lines = File.ReadAllLines("/proc/diskstats");
            int idx   = 0;

            foreach (var line in lines)
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 14) continue;
                var devName = parts[2];

                // Include only whole disk devices (sda, nvme0n1, vda, sdb, etc.)
                // Exclude partitions (sda1, sda2, nvme0n1p1, etc.)
                if (!IsWholeDisk(devName)) continue;

                if (!long.TryParse(parts[5],  out var readSectors))  continue;
                if (!long.TryParse(parts[9],  out var writeSectors)) continue;
                long ioTicks = parts.Length > 12 && long.TryParse(parts[12], out var iot) ? iot : 0;

                long readRate = 0, writeRate = 0;
                if (_prevDisk.TryGetValue(devName, out var prev))
                {
                    var readDelta  = readSectors  - prev.readSectors;
                    var writeDelta = writeSectors - prev.writeSectors;
                    readRate  = (long)Math.Max(0, readDelta  * 512L / elapsed);
                    writeRate = (long)Math.Max(0, writeDelta * 512L / elapsed);
                }
                _prevDisk[devName] = (readSectors, writeSectors);

                double activePercent = 0;
                if (_prevDiskIoTicks.TryGetValue(devName, out var prevIoTicks) && ioTicks >= prevIoTicks)
                {
                    var deltaMs    = ioTicks - prevIoTicks;
                    var elapsedMs  = elapsed * 1000.0;
                    activePercent  = Math.Min(100.0, elapsedMs > 0 ? deltaMs / elapsedMs * 100.0 : 0);
                }
                _prevDiskIoTicks[devName] = ioTicks;

                // Capacity via statvfs on typical mount point
                long totalBytes = 0, freeBytes = 0;
                var mountPath   = $"/dev/{devName}";
                // Try to find a mount point from /proc/mounts
                var mount = FindMountPoint(devName);
                if (mount != null)
                {
                    if (statvfs(mount, out var sv) == 0)
                    {
                        totalBytes = (long)(sv.f_blocks * sv.f_frsize);
                        freeBytes  = (long)(sv.f_bavail * sv.f_frsize);
                    }
                }

                result.Add(new DiskMetrics
                {
                    DiskIndex        = idx++,
                    DriveLetter      = mount ?? $"/dev/{devName}",
                    Label            = devName,
                    PhysicalName     = $"/dev/{devName}",
                    AllDriveLetters  = mount ?? string.Empty,
                    ReadBytesPerSec  = readRate,
                    WriteBytesPerSec = writeRate,
                    ActivePercent    = activePercent,
                    TotalBytes       = totalBytes,
                    FreeBytes        = freeBytes,
                });
            }
        }
        catch { }

        _prevDiskTime = now;
        return result;
    }

    private static bool IsWholeDisk(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        // nvme: nvme0n1 is whole, nvme0n1p1 is partition
        if (name.StartsWith("nvme", StringComparison.Ordinal))
            return !name.Contains('p') || name.IndexOf('p') < 6;
        // sd*, vd*, hd*: whole disk = pure letters (e.g. "sda"), partition = trailing digits (e.g. "sda1")
        if (name.StartsWith("sd", StringComparison.Ordinal) ||
            name.StartsWith("vd", StringComparison.Ordinal) ||
            name.StartsWith("hd", StringComparison.Ordinal))
        {
            var lettersEnd = 0;
            while (lettersEnd < name.Length && char.IsLetter(name[lettersEnd]))
                lettersEnd++;
            // afterLetters is empty → whole disk (pure letters like "sda")
            return lettersEnd == name.Length;
        }
        return false;
    }

    private string? FindMountPoint(string devName)
    {
        // Refresh cache if stale (mounts change very rarely — e.g. USB attach/detach)
        var now = DateTime.UtcNow;
        if ((now - _mountsCacheTime) >= MountsCacheDuration)
        {
            _mountsCache     = BuildMountsCache();
            _mountsCacheTime = now;
        }
        return _mountsCache.TryGetValue(devName, out var mp) ? mp : null;
    }

    private static Dictionary<string, string?> BuildMountsCache()
    {
        // Build devName → first-found-mountPoint map from /proc/mounts.
        // For whole disks whose partitions (not the disk itself) are mounted,
        // store the partition mount under the whole-disk name.
        var cache = new Dictionary<string, string?>(StringComparer.Ordinal);
        try
        {
            foreach (var line in File.ReadLines("/proc/mounts"))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                var dev  = parts[0];
                var mp   = parts[1];

                var lastSlash = dev.LastIndexOf('/');
                var devLeaf   = lastSlash >= 0 ? dev[(lastSlash + 1)..] : dev;
                if (string.IsNullOrEmpty(devLeaf)) continue;

                // Exact match: store devLeaf → mp
                if (!cache.ContainsKey(devLeaf))
                    cache[devLeaf] = mp;

                // Partition match: for "sda1" also store "sda" → mp (if not already set)
                // Find the whole-disk name by stripping trailing digits or 'p'+digits (nvme)
                var wholeDisk = StripPartitionSuffix(devLeaf);
                if (wholeDisk != devLeaf && !cache.ContainsKey(wholeDisk))
                    cache[wholeDisk] = mp;
            }
        }
        catch { }
        return cache;
    }

    private static string StripPartitionSuffix(string devLeaf)
    {
        if (string.IsNullOrEmpty(devLeaf)) return devLeaf;
        // nvme: "nvme0n1p2" → "nvme0n1"
        if (devLeaf.StartsWith("nvme", StringComparison.Ordinal))
        {
            var pIdx = devLeaf.LastIndexOf('p');
            if (pIdx > 4 && pIdx < devLeaf.Length - 1 && char.IsDigit(devLeaf[pIdx + 1]))
                return devLeaf[..pIdx];
        }
        // sd*, vd*, hd*: strip trailing digits
        int end = devLeaf.Length;
        while (end > 0 && char.IsDigit(devLeaf[end - 1])) end--;
        return end < devLeaf.Length ? devLeaf[..end] : devLeaf;
    }

    // ── Network (/proc/net/dev + .NET NetworkInterface + sysfs) ──────────────
    private IReadOnlyList<NetworkAdapterMetrics> ReadNetwork()
    {
        var result  = new List<NetworkAdapterMetrics>();
        var now     = DateTime.UtcNow;
        var elapsed = _prevNetTime == DateTime.MinValue
            ? 1.0
            : Math.Max(0.01, (now - _prevNetTime).TotalSeconds);

        // Build a name → NetworkInterface map for IP/speed/type enrichment
        var nicMap = new Dictionary<string, NetworkInterface>(StringComparer.Ordinal);
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                nicMap[nic.Name] = nic;
        }
        catch { }

        try
        {
            var lines = File.ReadAllLines("/proc/net/dev");
            // Skip 2-line header
            foreach (var line in lines.Skip(2))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var colonIdx = line.IndexOf(':');
                if (colonIdx < 0) continue;

                var name  = line[..colonIdx].Trim();
                if (name == "lo") continue;

                var vals  = line[(colonIdx + 1)..].Trim()
                                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (vals.Length < 9) continue;

                if (!long.TryParse(vals[0], out var rxBytes)) continue;
                if (!long.TryParse(vals[8], out var txBytes)) continue;

                long rxRate = 0, txRate = 0;
                if (_prevNet.TryGetValue(name, out var prev))
                {
                    rxRate = (long)Math.Max(0, (rxBytes - prev.rxBytes) / elapsed);
                    txRate = (long)Math.Max(0, (txBytes - prev.txBytes) / elapsed);
                }
                _prevNet[name] = (rxBytes, txBytes);

                // Enrich with IP, speed, and adapter type from NetworkInterface + sysfs
                string ipv4 = string.Empty, ipv6 = string.Empty, adapterType = "Ethernet";
                long linkSpeedBps = 0;
                bool isConnected  = true;

                if (nicMap.TryGetValue(name, out var nic))
                {
                    isConnected = nic.OperationalStatus == OperationalStatus.Up;
                    try
                    {
                        foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                        {
                            if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                                ipv4 = ua.Address.ToString();
                            else if (ua.Address.AddressFamily == AddressFamily.InterNetworkV6)
                                ipv6 = ua.Address.ToString();
                        }
                    }
                    catch { }
                }

                // Speed from /sys/class/net/{name}/speed (in Mbps, -1 if unavailable)
                try
                {
                    var speedPath = $"/sys/class/net/{name}/speed";
                    if (File.Exists(speedPath) && long.TryParse(File.ReadAllText(speedPath).Trim(), out var mbps) && mbps > 0)
                        linkSpeedBps = mbps * 1_000_000L;
                }
                catch { }

                // Type: wireless if /sys/class/net/{name}/wireless exists
                if (Directory.Exists($"/sys/class/net/{name}/wireless"))
                    adapterType = "WiFi";

                result.Add(new NetworkAdapterMetrics
                {
                    Name             = name,
                    Description      = name,
                    SendBytesPerSec  = txRate,
                    RecvBytesPerSec  = rxRate,
                    TotalSendBytes   = txBytes,
                    TotalRecvBytes   = rxBytes,
                    IsConnected      = isConnected,
                    IpAddress        = ipv4,
                    IPv4Address      = ipv4,
                    IPv6Address      = ipv6,
                    LinkSpeedBps     = linkSpeedBps,
                    AdapterType      = adapterType,
                });
            }
        }
        catch { }

        _prevNetTime = now;
        return result;
    }

    // ── GPU (NVIDIA via nvidia-smi; AMD via sysfs DRM) ────────────────────────
    private static IReadOnlyList<GpuMetrics> ReadGpu()
    {
        // Prefer NVIDIA if nvidia-smi is available
        if (_hasNvidiaSmi)
        {
            var nvResult = ReadNvidiaGpu();
            if (nvResult.Count > 0) return nvResult;
        }

        // Fall back to AMD sysfs
        return ReadAmdGpu();
    }

    private static IReadOnlyList<GpuMetrics> ReadNvidiaGpu()
    {
        try
        {
            var smiBin = File.Exists("/usr/bin/nvidia-smi") ? "/usr/bin/nvidia-smi" : "/usr/local/bin/nvidia-smi";
            using var proc = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo(
                    smiBin,
                    "--query-gpu=utilization.gpu,memory.used,memory.total,temperature.gpu,name --format=csv,noheader,nounits")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                }
            };
            proc.Start();
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            var result = new List<GpuMetrics>();
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(',');
                if (parts.Length < 5) continue;

                double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var usage);
                long.TryParse(parts[1].Trim(), out var memUsedMb);
                long.TryParse(parts[2].Trim(), out var memTotalMb);
                double.TryParse(parts[3].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var temp);
                var gpuName = parts[4].Trim();

                result.Add(new GpuMetrics
                {
                    Name                      = gpuName,
                    UsagePercent              = Math.Clamp(usage, 0, 100),
                    DedicatedMemoryUsedBytes  = memUsedMb  * 1_048_576L,
                    DedicatedMemoryTotalBytes = memTotalMb * 1_048_576L,
                    TemperatureCelsius        = temp,
                });
            }
            return result;
        }
        catch { return []; }
    }

    private static IReadOnlyList<GpuMetrics> ReadAmdGpu()
    {
        try
        {
            var busyPath = "/sys/class/drm/card0/device/gpu_busy_percent";
            var vramUsed = "/sys/class/drm/card0/device/mem_info_vram_used";
            var vramTot  = "/sys/class/drm/card0/device/mem_info_vram_total";

            if (!File.Exists(busyPath)) return [];

            double busy = 0;
            long   used = 0, total = 0;

            var busyTxt = File.ReadAllText(busyPath).Trim();
            double.TryParse(busyTxt, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out busy);

            if (File.Exists(vramUsed)) long.TryParse(File.ReadAllText(vramUsed).Trim(), out used);
            if (File.Exists(vramTot))  long.TryParse(File.ReadAllText(vramTot).Trim(),  out total);

            return
            [
                new GpuMetrics
                {
                    Name                       = "GPU",
                    UsagePercent               = Math.Clamp(busy, 0, 100),
                    DedicatedMemoryUsedBytes   = used,
                    DedicatedMemoryTotalBytes  = total,
                    TemperatureCelsius         = 0,
                },
            ];
        }
        catch { return []; }
    }

    // ── Static helpers ─────────────────────────────────────────────────────────
    private static string ReadCpuModel()
    {
        try
        {
            foreach (var line in File.ReadAllLines("/proc/cpuinfo"))
            {
                if (line.StartsWith("model name", StringComparison.OrdinalIgnoreCase))
                {
                    var colon = line.IndexOf(':');
                    return colon >= 0 ? line[(colon + 1)..].Trim() : line;
                }
            }
        }
        catch { }
        return string.Empty;
    }

    private static int ReadPhysicalCores()
    {
        try
        {
            // Track (physical_id, core_id) pairs to handle multi-socket systems
            // where core IDs repeat across sockets.
            var coreIds = new HashSet<(string physId, string coreId)>();
            string curPhysId = "0";
            foreach (var line in File.ReadAllLines("/proc/cpuinfo"))
            {
                if (line.StartsWith("physical id", StringComparison.OrdinalIgnoreCase))
                {
                    var colon = line.IndexOf(':');
                    if (colon >= 0) curPhysId = line[(colon + 1)..].Trim();
                }
                else if (line.StartsWith("core id", StringComparison.OrdinalIgnoreCase))
                {
                    var colon = line.IndexOf(':');
                    if (colon >= 0) coreIds.Add((curPhysId, line[(colon + 1)..].Trim()));
                }
            }
            if (coreIds.Count > 0) return coreIds.Count;
        }
        catch { }
        return Environment.ProcessorCount;
    }

    private static long ReadTotalMem()
    {
        try
        {
            foreach (var line in File.ReadAllLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                    return ParseMeminfoKb(line) * 1024L;
            }
        }
        catch { }
        return 0;
    }

    private static double ReadHwmonTemperature()
    {
        // Prefer named hwmon drivers: coretemp (Intel), k10temp / zenpower (AMD)
        var preferred = new HashSet<string>(["coretemp", "k10temp", "zenpower"], StringComparer.OrdinalIgnoreCase);
        var hwmonBase = "/sys/class/hwmon";

        if (Directory.Exists(hwmonBase))
        {
            // First pass: preferred drivers
            foreach (var hwmon in Directory.GetDirectories(hwmonBase))
            {
                var namePath = Path.Combine(hwmon, "name");
                if (!File.Exists(namePath)) continue;
                var driverName = File.ReadAllText(namePath).Trim();
                if (!preferred.Contains(driverName)) continue;

                var temp = ReadFirstTempInput(hwmon);
                if (temp > 0) return temp;
            }

            // Second pass: any hwmon with temp input
            foreach (var hwmon in Directory.GetDirectories(hwmonBase))
            {
                var temp = ReadFirstTempInput(hwmon);
                if (temp > 0) return temp;
            }
        }

        // Fallback: scan all thermal_zone* entries
        var thermalBase = "/sys/class/thermal";
        if (Directory.Exists(thermalBase))
        {
            foreach (var zone in Directory.GetDirectories(thermalBase, "thermal_zone*"))
            {
                var tempPath = Path.Combine(zone, "temp");
                if (!File.Exists(tempPath)) continue;
                var txt = File.ReadAllText(tempPath).Trim();
                if (long.TryParse(txt, out var mC) && mC > 0)
                    return mC / 1000.0;
            }
        }

        return 0;
    }

    private static double ReadFirstTempInput(string hwmonDir)
    {
        // Try temp1_input, temp2_input, ...
        for (int i = 1; i <= 16; i++)
        {
            var path = Path.Combine(hwmonDir, $"temp{i}_input");
            if (!File.Exists(path)) continue;
            var txt = File.ReadAllText(path).Trim();
            if (long.TryParse(txt, out var mC) && mC > 0)
                return mC / 1000.0;
        }
        return 0;
    }
}
