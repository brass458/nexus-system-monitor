using NexusMonitor.Core.Models;

namespace NexusMonitor.Platform.Linux;

public sealed class LinuxHardwareInfoProvider
{
    public Task<SystemHardwareInfo> QueryAsync(CancellationToken ct = default) =>
        Task.Run(BuildInfo, ct);

    private static SystemHardwareInfo BuildInfo()
    {
        var uptime    = ReadUptime();
        var cpuName   = ReadCpuModel();
        var (physical, logical) = ReadCoreCounts();
        var (l2Kb, l3Kb)        = ReadCacheSizes();
        var maxFreqMhz           = ReadMaxFreqMhz();
        var stepping             = ReadCpuStepping();
        var totalRamBytes        = ReadTotalMem();

        var cpu = new CpuHardwareInfo(
            Name:          cpuName,
            Architecture:  System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            PhysicalCores: physical,
            LogicalCores:  logical,
            L2CacheKB:     (int)l2Kb,
            L3CacheKB:     (int)l3Kb,
            MaxClockMhz:   (int)maxFreqMhz,
            Socket:        ReadDmiField("board_name"),
            Stepping:      stepping);

        var gpus    = ReadGpus();
        var storage = ReadStorage();

        return new SystemHardwareInfo(
            Hostname:               Environment.MachineName,
            OsName:                 System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            OsBuild:                Environment.OSVersion.ToString(),
            OsArchitecture:         System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            Uptime:                 uptime,
            BiosVendor:             ReadDmiField("bios_vendor"),
            BiosVersion:            ReadDmiField("bios_version"),
            MotherboardManufacturer: ReadDmiField("board_vendor"),
            MotherboardModel:       ReadDmiField("board_name"),
            Cpu:                    cpu,
            TotalRamBytes:          totalRamBytes,
            RamSlots:               [],
            Gpus:                   gpus,
            Storage:                storage);
    }

    // ── Uptime ────────────────────────────────────────────────────────────────
    private static TimeSpan ReadUptime()
    {
        try
        {
            var txt = File.ReadAllText("/proc/uptime").Trim().Split(' ')[0];
            if (double.TryParse(txt, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var secs))
                return TimeSpan.FromSeconds(secs);
        }
        catch { }
        return TimeSpan.FromMilliseconds(Environment.TickCount64);
    }

    // ── DMI / sysfs fields ────────────────────────────────────────────────────
    private static string ReadDmiField(string name)
    {
        try
        {
            var path = $"/sys/class/dmi/id/{name}";
            return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
        }
        catch { return string.Empty; }
    }

    // ── CPU model ─────────────────────────────────────────────────────────────
    private static string ReadCpuModel()
    {
        try
        {
            foreach (var line in File.ReadAllLines("/proc/cpuinfo"))
            {
                if (line.StartsWith("model name", StringComparison.OrdinalIgnoreCase))
                {
                    var idx = line.IndexOf(':');
                    if (idx >= 0) return line[(idx + 1)..].Trim();
                }
            }
        }
        catch { }
        return System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString();
    }

    // ── Core counts ───────────────────────────────────────────────────────────
    private static (int physical, int logical) ReadCoreCounts()
    {
        var logical = Environment.ProcessorCount;
        try
        {
            var seen = new HashSet<(int physId, int coreId)>();
            int physId = 0, coreId = 0;
            foreach (var line in File.ReadAllLines("/proc/cpuinfo"))
            {
                if (line.StartsWith("physical id", StringComparison.OrdinalIgnoreCase))
                {
                    var idx = line.IndexOf(':');
                    if (idx >= 0) int.TryParse(line[(idx + 1)..].Trim(), out physId);
                }
                else if (line.StartsWith("core id", StringComparison.OrdinalIgnoreCase))
                {
                    var idx = line.IndexOf(':');
                    if (idx >= 0) int.TryParse(line[(idx + 1)..].Trim(), out coreId);
                }
                else if (string.IsNullOrWhiteSpace(line))
                {
                    seen.Add((physId, coreId));
                }
            }
            if (seen.Count > 0) return (seen.Count, logical);
        }
        catch { }
        return (logical, logical);
    }

    // ── Cache sizes ───────────────────────────────────────────────────────────
    private static (long l2Kb, long l3Kb) ReadCacheSizes()
    {
        long l2 = 0, l3 = 0;
        try
        {
            // Walk cache indices for cpu0
            var cacheBase = "/sys/devices/system/cpu/cpu0/cache";
            if (!Directory.Exists(cacheBase)) return (0, 0);

            foreach (var dir in Directory.GetDirectories(cacheBase, "index*"))
            {
                var levelPath = Path.Combine(dir, "level");
                var sizePath  = Path.Combine(dir, "size");
                if (!File.Exists(levelPath) || !File.Exists(sizePath)) continue;

                if (!int.TryParse(File.ReadAllText(levelPath).Trim(), out var level)) continue;
                var sizeStr = File.ReadAllText(sizePath).Trim();

                long sizeKb = 0;
                if (sizeStr.EndsWith("K", StringComparison.OrdinalIgnoreCase))
                    long.TryParse(sizeStr[..^1], out sizeKb);
                else
                    long.TryParse(sizeStr, out sizeKb);

                if (level == 2 && sizeKb > l2) l2 = sizeKb;
                if (level == 3 && sizeKb > l3) l3 = sizeKb;
            }
        }
        catch { }
        return (l2, l3);
    }

    // ── Max CPU frequency ─────────────────────────────────────────────────────
    private static double ReadMaxFreqMhz()
    {
        try
        {
            var path = "/sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_max_freq";
            if (File.Exists(path))
            {
                var txt = File.ReadAllText(path).Trim();
                if (long.TryParse(txt, out var kHz)) return kHz / 1000.0;
            }
        }
        catch { }
        return 0;
    }

    // ── CPU stepping ──────────────────────────────────────────────────────────
    private static string ReadCpuStepping()
    {
        try
        {
            foreach (var line in File.ReadAllLines("/proc/cpuinfo"))
            {
                if (line.StartsWith("stepping", StringComparison.OrdinalIgnoreCase))
                {
                    var idx = line.IndexOf(':');
                    if (idx >= 0) return line[(idx + 1)..].Trim();
                }
            }
        }
        catch { }
        return string.Empty;
    }

    // ── Total RAM ─────────────────────────────────────────────────────────────
    private static long ReadTotalMem()
    {
        try
        {
            foreach (var line in File.ReadAllLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length < 2) continue;
                    var val = parts[1].Trim().Split(' ')[0];
                    if (long.TryParse(val, out var kb)) return kb * 1024L;
                }
            }
        }
        catch { }
        return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    }

    // ── GPUs via /sys/class/drm ────────────────────────────────────────────────
    private static IReadOnlyList<GpuHardwareInfo> ReadGpus()
    {
        var result = new List<GpuHardwareInfo>();
        try
        {
            var drmBase = "/sys/class/drm";
            if (!Directory.Exists(drmBase)) return result;

            var seen = new HashSet<string>();
            foreach (var card in Directory.GetDirectories(drmBase, "card*"))
            {
                // Only top-level cards, not card0-HDMI-A-1 etc.
                var name = Path.GetFileName(card);
                if (name.Contains('-')) continue;

                var vendorPath = Path.Combine(card, "device", "vendor");
                var devicePath = Path.Combine(card, "device", "device");

                var vendorId = File.Exists(vendorPath) ? File.ReadAllText(vendorPath).Trim() : "";
                var deviceId = File.Exists(devicePath) ? File.ReadAllText(devicePath).Trim() : "";

                var key = $"{vendorId}:{deviceId}";
                if (!seen.Add(key)) continue;

                var vendorName = vendorId switch
                {
                    "0x10de" => "NVIDIA",
                    "0x1002" => "AMD",
                    "0x8086" => "Intel",
                    _        => vendorId
                };

                result.Add(new GpuHardwareInfo(
                    Name:           $"{vendorName} GPU ({name})",
                    DriverVersion:  string.Empty,
                    VramBytes:      0,
                    VideoProcessor: deviceId,
                    Status:         string.Empty));
            }
        }
        catch { }
        return result;
    }

    // ── Storage via /sys/block ────────────────────────────────────────────────
    private static IReadOnlyList<StorageDriveInfo> ReadStorage()
    {
        var result = new List<StorageDriveInfo>();
        try
        {
            var blockBase = "/sys/block";
            if (!Directory.Exists(blockBase)) return result;

            foreach (var dev in Directory.GetDirectories(blockBase))
            {
                var devName = Path.GetFileName(dev);
                // Skip loop, ram, dm devices
                if (devName.StartsWith("loop", StringComparison.Ordinal) ||
                    devName.StartsWith("ram",  StringComparison.Ordinal) ||
                    devName.StartsWith("dm-",  StringComparison.Ordinal) ||
                    devName.StartsWith("zram", StringComparison.Ordinal)) continue;

                var modelPath = Path.Combine(dev, "device", "model");
                var sizePath  = Path.Combine(dev, "size");

                var model = File.Exists(modelPath) ? File.ReadAllText(modelPath).Trim() : devName;
                long sizeBytes = 0;
                if (File.Exists(sizePath) && long.TryParse(File.ReadAllText(sizePath).Trim(), out var sectors))
                    sizeBytes = sectors * 512L;

                result.Add(new StorageDriveInfo(
                    Index:        0,
                    Model:        model,
                    Interface:    string.Empty,
                    SizeBytes:    sizeBytes,
                    MediaType:    string.Empty,
                    SerialNumber: string.Empty,
                    Status:       string.Empty));
            }
        }
        catch { }
        return result;
    }
}
