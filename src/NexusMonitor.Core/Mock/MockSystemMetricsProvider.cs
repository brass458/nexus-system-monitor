using System.Reactive.Linq;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Mock;

public sealed class MockSystemMetricsProvider : ISystemMetricsProvider
{
    private static readonly Random _rng = new(99);
    private static int _tick;

    public IObservable<SystemMetrics> GetMetricsStream(TimeSpan interval)
        => Observable.Interval(interval)
                     .Select(_ => BuildMetrics())
                     .StartWith(BuildMetrics());

    public Task<SystemMetrics> GetMetricsAsync(CancellationToken ct = default)
        => Task.FromResult(BuildMetrics());

    private static SystemMetrics BuildMetrics()
    {
        var t = (double)Interlocked.Increment(ref _tick) * 0.1;
        var cpuTotal = 15 + 20 * Math.Abs(Math.Sin(t * 0.5)) + 5 * Math.Abs(Math.Sin(t * 1.7));
        var cores = 16;

        return new SystemMetrics
        {
            Cpu = new CpuMetrics
            {
                TotalPercent = Math.Min(100, cpuTotal),
                CorePercents = Enumerable.Range(0, cores)
                    .Select(i => Math.Min(100, Math.Max(0,
                        cpuTotal + 10 * Math.Sin(t * 0.8 + i * 0.4) + _rng.NextDouble() * 5)))
                    .ToArray(),
                FrequencyMhz = 3600 + 400 * Math.Sin(t * 0.2),
                TemperatureCelsius = 45 + 15 * Math.Abs(Math.Sin(t * 0.3)),
                LogicalCores = cores,
                PhysicalCores = cores / 2,
                ModelName = "AMD Ryzen 9 7950X"
            },
            Memory = new MemoryMetrics
            {
                TotalBytes = 32L * 1024 * 1024 * 1024,
                UsedBytes = (long)(12L * 1024 * 1024 * 1024 + 2L * 1024 * 1024 * 1024 * Math.Abs(Math.Sin(t * 0.1))),
                CachedBytes = 4L * 1024 * 1024 * 1024,
                PagedPoolBytes = 512L * 1024 * 1024,
                NonPagedPoolBytes = 256L * 1024 * 1024,
                CommitTotalBytes = (long)(14L * 1024 * 1024 * 1024),
                CommitLimitBytes = 48L * 1024 * 1024 * 1024
            },
            Disks =
            [
                new DiskMetrics
                {
                    DriveLetter = "C:",
                    Label = "System",
                    ReadBytesPerSec = (long)(Math.Abs(Math.Sin(t * 0.6)) * 50_000_000),
                    WriteBytesPerSec = (long)(Math.Abs(Math.Sin(t * 0.9)) * 20_000_000),
                    ActivePercent = Math.Min(100, 5 + 15 * Math.Abs(Math.Sin(t * 0.7))),
                    TotalBytes = 2L * 1024 * 1024 * 1024 * 1024,
                    FreeBytes = 800L * 1024 * 1024 * 1024,
                    DiskIndex = 0, PhysicalName = "Physical Drive 0", AllDriveLetters = "C:"
                },
                new DiskMetrics
                {
                    DriveLetter = "D:",
                    Label = "Data",
                    ReadBytesPerSec = (long)(Math.Abs(Math.Sin(t * 0.4)) * 10_000_000),
                    WriteBytesPerSec = (long)(Math.Abs(Math.Sin(t * 0.3)) * 5_000_000),
                    ActivePercent = Math.Min(100, 2 + 8 * Math.Abs(Math.Sin(t * 0.4))),
                    TotalBytes = 4L * 1024 * 1024 * 1024 * 1024,
                    FreeBytes = 2_500L * 1024 * 1024 * 1024,
                    DiskIndex = 1, PhysicalName = "Physical Drive 1", AllDriveLetters = "D:"
                }
            ],
            NetworkAdapters =
            [
                new NetworkAdapterMetrics
                {
                    Name = "Ethernet",
                    Description = "Intel(R) Ethernet Connection I219-V",
                    SendBytesPerSec = (long)(Math.Abs(Math.Sin(t * 1.1)) * 2_000_000),
                    RecvBytesPerSec = (long)(Math.Abs(Math.Sin(t * 0.8)) * 8_000_000),
                    TotalSendBytes = 15L * 1024 * 1024 * 1024,
                    TotalRecvBytes = 80L * 1024 * 1024 * 1024,
                    IsConnected = true,
                    IpAddress = "192.168.1.100",
                    IPv4Address = "192.168.1.100", IPv6Address = "fe80::1", LinkSpeedBps = 1_000_000_000L, AdapterType = "Ethernet"
                }
            ],
            Gpus =
            [
                new GpuMetrics
                {
                    Name = "NVIDIA GeForce RTX 4080",
                    UsagePercent = Math.Min(100, 5 + 30 * Math.Abs(Math.Sin(t * 0.4))),
                    DedicatedMemoryUsedBytes = (long)(4L * 1024 * 1024 * 1024 * Math.Abs(Math.Sin(t * 0.2)) + 2L * 1024 * 1024 * 1024),
                    DedicatedMemoryTotalBytes = 16L * 1024 * 1024 * 1024,
                    TemperatureCelsius = 40 + 20 * Math.Abs(Math.Sin(t * 0.3)),
                    Engine3DPercent = Math.Min(100, 5 + 30 * Math.Abs(Math.Sin(t * 0.4))), EngineCopyPercent = 2.0, EngineVideoDecodePercent = 1.0, EngineVideoEncodePercent = 0.5, SharedMemoryUsedBytes = 512L * 1024 * 1024
                }
            ]
        };
    }
}
