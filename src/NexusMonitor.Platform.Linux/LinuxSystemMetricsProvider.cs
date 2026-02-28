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

    private readonly Dictionary<string, (long readSectors, long writeSectors)> _prevDisk = new();
    private DateTime _prevDiskTime = DateTime.MinValue;

    private readonly Dictionary<string, (long rxBytes, long txBytes)> _prevNet = new();
    private DateTime _prevNetTime = DateTime.MinValue;

    public LinuxSystemMetricsProvider()
    {
        _cpuModel      = ReadCpuModel();
        _logicalCores  = Environment.ProcessorCount;
        _physicalCores = ReadPhysicalCores();
        _totalMemBytes = ReadTotalMem();
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

            // Per-core lines: cpu0 cpu1 ...
            for (int i = 0; ; i++)
            {
                var coreLine = lines.FirstOrDefault(l => l.StartsWith($"cpu{i} ", StringComparison.Ordinal));
                if (coreLine == null) break;
                var fields = ParseCpuLine(coreLine);
                if (fields.Length >= 4)
                {
                    var total = fields.Sum();
                    var idle  = fields[3];
                    double pct = total > 0 ? Math.Clamp((double)(total - idle) / total * 100.0, 0, 100) : 0;
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

        // Temperature
        try
        {
            var tempPath = "/sys/class/thermal/thermal_zone0/temp";
            if (File.Exists(tempPath))
            {
                var txt = File.ReadAllText(tempPath).Trim();
                if (long.TryParse(txt, out var mC)) tempC = mC / 1000.0;
            }
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

                long readRate = 0, writeRate = 0;
                if (_prevDisk.TryGetValue(devName, out var prev))
                {
                    var readDelta  = readSectors  - prev.readSectors;
                    var writeDelta = writeSectors - prev.writeSectors;
                    readRate  = (long)Math.Max(0, readDelta  * 512L / elapsed);
                    writeRate = (long)Math.Max(0, writeDelta * 512L / elapsed);
                }
                _prevDisk[devName] = (readSectors, writeSectors);

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
                    ActivePercent    = 0,
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
        // sd*, vd*, hd*: whole disk has no trailing digit after letters
        if (name.StartsWith("sd", StringComparison.Ordinal) ||
            name.StartsWith("vd", StringComparison.Ordinal) ||
            name.StartsWith("hd", StringComparison.Ordinal))
        {
            // "sda" = whole, "sda1" = partition
            var lettersEnd = 0;
            while (lettersEnd < name.Length && char.IsLetter(name[lettersEnd]))
                lettersEnd++;
            var afterLetters = name[lettersEnd..];
            return afterLetters.Length == 1 && char.IsLetter(afterLetters[0]);
        }
        return false;
    }

    private static string? FindMountPoint(string devName)
    {
        try
        {
            foreach (var line in File.ReadAllLines("/proc/mounts"))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                if (parts[0].EndsWith(devName, StringComparison.Ordinal))
                    return parts[1];
            }
        }
        catch { }
        return null;
    }

    // ── Network (/proc/net/dev) ────────────────────────────────────────────────
    private IReadOnlyList<NetworkAdapterMetrics> ReadNetwork()
    {
        var result  = new List<NetworkAdapterMetrics>();
        var now     = DateTime.UtcNow;
        var elapsed = _prevNetTime == DateTime.MinValue
            ? 1.0
            : Math.Max(0.01, (now - _prevNetTime).TotalSeconds);

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

                result.Add(new NetworkAdapterMetrics
                {
                    Name             = name,
                    Description      = name,
                    SendBytesPerSec  = txRate,
                    RecvBytesPerSec  = rxRate,
                    TotalSendBytes   = txBytes,
                    TotalRecvBytes   = rxBytes,
                    IsConnected      = true,
                    IpAddress        = string.Empty,
                    IPv4Address      = string.Empty,
                    IPv6Address      = string.Empty,
                    LinkSpeedBps     = 0,
                    AdapterType      = "Unknown",
                });
            }
        }
        catch { }

        _prevNetTime = now;
        return result;
    }

    // ── GPU (sysfs DRM) ────────────────────────────────────────────────────────
    private static IReadOnlyList<GpuMetrics> ReadGpu()
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
            var cores = new HashSet<string>();
            foreach (var line in File.ReadAllLines("/proc/cpuinfo"))
            {
                if (line.StartsWith("core id", StringComparison.OrdinalIgnoreCase))
                {
                    var colon = line.IndexOf(':');
                    if (colon >= 0) cores.Add(line[(colon + 1)..].Trim());
                }
            }
            if (cores.Count > 0) return cores.Count;
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
}
