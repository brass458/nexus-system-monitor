using System.Diagnostics;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Net.NetworkInformation;
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

    // ── Subprocess helper ──────────────────────────────────────────────────────
    private static string RunCommand(string cmd, string args)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo(cmd, args)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                }
            };
            proc.Start();
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }

    // ── Cached static data (read once at startup) ──────────────────────────────
    private readonly string _cpuModel;
    private readonly int    _physicalCores;
    private readonly int    _logicalCores;
    private readonly long   _totalMemBytes;
    private readonly int    _pageSize;

    // ── Delta tracking for CPU and disk/network rates ─────────────────────────
    private long[]  _prevCpuTimes = [];          // [user, nice, sys, idle] summed
    private DateTime _prevCpuTime = DateTime.MinValue;

    private readonly Dictionary<string, (long rxBytes, long txBytes)> _prevNetBytes = new();
    private DateTime _prevNetTime = DateTime.MinValue;

    private readonly Dictionary<string, (long readSectors, long writeSectors)> _prevDiskSectors = new();
    private DateTime _prevDiskTime = DateTime.MinValue;

    public MacOSSystemMetricsProvider()
    {
        _cpuModel      = SysctlString("machdep.cpu.brand_string");
        _physicalCores = SysctlInt("hw.physicalcpu");
        _logicalCores  = Environment.ProcessorCount;
        _totalMemBytes = SysctlLong("hw.memsize");
        _pageSize      = SysctlInt("hw.pagesize");

        if (_physicalCores <= 0) _physicalCores = _logicalCores;
        if (_pageSize <= 0)      _pageSize      = 4096;
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
        var memory  = ReadMemory();
        var cpu     = ReadCpu();
        var disks   = ReadDisks();
        var nets    = ReadNetwork();

        return new SystemMetrics
        {
            Cpu             = cpu,
            Memory          = memory,
            Disks           = disks,
            NetworkAdapters = nets,
            Gpus            = [],
            Timestamp       = DateTime.UtcNow,
        };
    }

    // ── Memory via vm_stat ─────────────────────────────────────────────────────
    private MemoryMetrics ReadMemory()
    {
        // Fast path: sysctl for available pages
        var freePages      = (long)(uint)SysctlInt("vm.page_free_count");
        var availableBytes = freePages * _pageSize;

        // Enrich with vm_stat output for cached pages
        long cachedBytes = 0;
        var vmOut = RunCommand("vm_stat", string.Empty);
        if (!string.IsNullOrEmpty(vmOut))
        {
            long specPages   = ParseVmStatLine(vmOut, "Pages speculative:");
            long inactPages  = ParseVmStatLine(vmOut, "Pages inactive:");
            long wirePages   = ParseVmStatLine(vmOut, "Pages wired down:");
            long freeVm      = ParseVmStatLine(vmOut, "Pages free:");

            // Use vm_stat free if sysctl returned 0
            if (freePages == 0 && freeVm > 0)
            {
                freePages      = freeVm + specPages;
                availableBytes = freePages * _pageSize;
            }
            cachedBytes = inactPages * _pageSize;
        }

        var usedBytes = _totalMemBytes > 0
            ? Math.Max(0L, _totalMemBytes - availableBytes)
            : 0L;

        return new MemoryMetrics
        {
            TotalBytes          = _totalMemBytes,
            AvailableBytes      = availableBytes,
            UsedBytes           = usedBytes,
            CachedBytes         = cachedBytes,
            PagedPoolBytes      = 0,
            NonPagedPoolBytes   = 0,
            CommitTotalBytes    = 0,
            CommitLimitBytes    = 0,
        };
    }

    private static long ParseVmStatLine(string text, string key)
    {
        var idx = text.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0) return 0;
        var rest = text[(idx + key.Length)..].TrimStart();
        var end  = rest.IndexOfAny(['.', '\n', '\r', ' ']);
        var num  = end >= 0 ? rest[..end] : rest;
        return long.TryParse(num.Trim(), out var v) ? v : 0;
    }

    // ── CPU via top / sysctl ───────────────────────────────────────────────────
    private CpuMetrics ReadCpu()
    {
        double totalPercent = 0;
        double freqMhz      = 0;

        // Frequency via sysctl
        var freqHz = SysctlLong("hw.cpufrequency");
        if (freqHz == 0)
            freqHz = SysctlLong("hw.cpufrequency_max");
        if (freqHz > 0)
            freqMhz = freqHz / 1_000_000.0;

        // CPU% via top -l 1
        var topOut = RunCommand("top", "-l 1 -stats pid,cpu -n 0");
        if (!string.IsNullOrEmpty(topOut))
        {
            // Look for: "CPU usage: X.X% user, Y.Y% sys, Z.Z% idle"
            var cpuLine = topOut.Split('\n')
                                .FirstOrDefault(l => l.Contains("CPU usage:", StringComparison.OrdinalIgnoreCase));
            if (cpuLine != null)
            {
                double user = 0, sys = 0, idle = 0;
                ExtractPercent(cpuLine, "user", ref user);
                ExtractPercent(cpuLine, "sys",  ref sys);
                ExtractPercent(cpuLine, "idle", ref idle);
                totalPercent = Math.Clamp(user + sys, 0, 100);
            }
        }

        return new CpuMetrics
        {
            TotalPercent       = totalPercent,
            CorePercents       = [],
            FrequencyMhz       = freqMhz,
            TemperatureCelsius = 0,
            LogicalCores       = _logicalCores,
            PhysicalCores      = _physicalCores,
            ModelName          = _cpuModel,
        };
    }

    private static void ExtractPercent(string line, string keyword, ref double value)
    {
        var idx = line.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return;
        // Scan backwards for the number
        int end = idx - 1;
        while (end >= 0 && line[end] == ' ') end--;
        if (end < 0) return;
        // end should be on '%' or after the digits
        if (line[end] == '%') end--;
        int start = end;
        while (start > 0 && (char.IsDigit(line[start - 1]) || line[start - 1] == '.'))
            start--;
        if (double.TryParse(line[start..(end + 1)], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var v))
            value = v;
    }

    // ── Disks via df -k ────────────────────────────────────────────────────────
    private IReadOnlyList<DiskMetrics> ReadDisks()
    {
        var result = new List<DiskMetrics>();
        var dfOut  = RunCommand("df", "-k");
        if (string.IsNullOrEmpty(dfOut)) return result;

        int index = 0;
        foreach (var line in dfOut.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 6) continue;
            // Filesystem 1K-blocks Used Available Use% Mounted-on
            // Skip header
            if (parts[0] == "Filesystem") continue;
            // Only include real block devices (starts with /dev/)
            if (!parts[0].StartsWith("/dev/", StringComparison.Ordinal)) continue;

            if (!long.TryParse(parts[1], out var totalKb))  continue;
            if (!long.TryParse(parts[3], out var availKb))  continue;

            var mountPoint  = parts[^1];
            var driveLetter = mountPoint == "/" ? "/" : mountPoint;

            result.Add(new DiskMetrics
            {
                DiskIndex       = index++,
                DriveLetter     = driveLetter,
                Label           = parts[0],
                PhysicalName    = parts[0],
                AllDriveLetters = driveLetter,
                TotalBytes      = totalKb  * 1024L,
                FreeBytes       = availKb  * 1024L,
                ReadBytesPerSec = 0,
                WriteBytesPerSec = 0,
                ActivePercent   = 0,
            });
        }

        return result;
    }

    // ── Network via NetworkInterface ───────────────────────────────────────────
    private IReadOnlyList<NetworkAdapterMetrics> ReadNetwork()
    {
        var result  = new List<NetworkAdapterMetrics>();
        var now     = DateTime.UtcNow;
        var elapsed = _prevNetTime == DateTime.MinValue
            ? 1.0
            : Math.Max(0.01, (now - _prevNetTime).TotalSeconds);

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var stats   = nic.GetIPStatistics();
                var rxBytes = stats.BytesReceived;
                var txBytes = stats.BytesSent;

                long rxRate = 0, txRate = 0;
                if (_prevNetBytes.TryGetValue(nic.Name, out var prev))
                {
                    rxRate = (long)Math.Max(0, (rxBytes - prev.rxBytes) / elapsed);
                    txRate = (long)Math.Max(0, (txBytes - prev.txBytes) / elapsed);
                }
                _prevNetBytes[nic.Name] = (rxBytes, txBytes);

                var ipv4 = string.Empty;
                var ipv6 = string.Empty;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        ipv4 = ua.Address.ToString();
                    else if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                        ipv6 = ua.Address.ToString();
                }

                result.Add(new NetworkAdapterMetrics
                {
                    Name             = nic.Name,
                    Description      = nic.Description,
                    SendBytesPerSec  = txRate,
                    RecvBytesPerSec  = rxRate,
                    TotalSendBytes   = txBytes,
                    TotalRecvBytes   = rxBytes,
                    IsConnected      = nic.OperationalStatus == OperationalStatus.Up,
                    IpAddress        = ipv4,
                    IPv4Address      = ipv4,
                    IPv6Address      = ipv6,
                    LinkSpeedBps     = nic.Speed,
                    AdapterType      = nic.NetworkInterfaceType.ToString(),
                });
            }
        }
        catch { }

        _prevNetTime = now;
        return result;
    }
}
