using System.Reactive.Linq;
using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Health;

/// <summary>
/// Subscribes to the metrics and process streams, computes a
/// <see cref="SystemHealthSnapshot"/> every tick, and exposes it as an Rx observable.
/// </summary>
public sealed class SystemHealthService : IDisposable
{
    private readonly ISystemMetricsProvider _metrics;
    private readonly IProcessProvider       _processes;
    private readonly AppSettings            _settings;
    private readonly ILogger<SystemHealthService> _logger;

    private readonly BehaviorSubject<SystemHealthSnapshot> _subject =
        new(new SystemHealthSnapshot());

    private IDisposable? _subscription;

    // Rolling history for trend computation (60 samples ≈ 2 min at 2s interval)
    private const int HistorySize = 60;
    private readonly Queue<double> _cpuHistory    = new(HistorySize);
    private readonly Queue<double> _memHistory    = new(HistorySize);
    private readonly Queue<double> _diskHistory   = new(HistorySize);
    private readonly Queue<double> _gpuHistory    = new(HistorySize);
    private readonly Queue<double> _overallHistory = new(HistorySize);

    public IObservable<SystemHealthSnapshot> HealthStream => _subject.AsObservable();
    public SystemHealthSnapshot Current => _subject.Value;

    public SystemHealthService(
        ISystemMetricsProvider metrics,
        IProcessProvider       processes,
        AppSettings            settings,
        ILogger<SystemHealthService>? logger = null)
    {
        _metrics   = metrics;
        _processes = processes;
        _settings  = settings;
        _logger    = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SystemHealthService>.Instance;
    }

    public void Start(TimeSpan interval)
    {
        // Dispose any previous subscription first so interval changes take effect
        Stop();

        var metricsObs  = _metrics.GetMetricsStream(interval);
        var processObs  = _processes.GetProcessStream(interval);

        _subscription = metricsObs
            .CombineLatest(processObs, (m, p) => (Metrics: m, Processes: p))
            .Subscribe(
                data => Update(data.Metrics, data.Processes),
                ex => { _logger.LogError(ex, "SystemHealthService stream faulted"); });
    }

    public void Stop()
    {
        _subscription?.Dispose();
        _subscription = null;
    }

    private void Update(SystemMetrics m, IReadOnlyList<ProcessInfo> processes)
    {
        // ── Raw values ────────────────────────────────────────────────────────
        var cpuVal  = m.Cpu.TotalPercent;
        var memVal  = m.Memory.UsedPercent;
        var diskVal = m.Disks.Count > 0 ? m.Disks.Average(d => d.ActivePercent) : 0;
        var diskUsed = m.Disks.Count > 0 ? m.Disks.Max(d => d.UsedPercent) : 0;
        var gpuVal  = m.Gpus.Count  > 0 ? m.Gpus.Average(g => g.UsagePercent) : 0;
        var cpuTemp = m.Cpu.TemperatureCelsius;
        var gpuTemp = m.Gpus.Count  > 0 ? m.Gpus.Average(g => g.TemperatureCelsius) : -1;

        // ── Scores ────────────────────────────────────────────────────────────
        var cpuScore    = HealthScoring.ScoreCpu(cpuVal);
        var memScore    = HealthScoring.ScoreMemory(memVal);
        var diskScore   = HealthScoring.ScoreDisk(diskVal, diskUsed);
        var gpuScore    = HealthScoring.ScoreGpu(gpuVal);
        var thermalScore = HealthScoring.ScoreThermal(cpuTemp, gpuTemp);
        var overall     = HealthScoring.CompositeScore(cpuScore, memScore, diskScore, gpuScore, thermalScore);

        // ── History + trends ─────────────────────────────────────────────────
        Enqueue(_cpuHistory,     cpuScore);
        Enqueue(_memHistory,     memScore);
        Enqueue(_diskHistory,    diskScore);
        Enqueue(_gpuHistory,     gpuScore);
        Enqueue(_overallHistory, overall);

        // ── Top consumers (by impact) ─────────────────────────────────────────
        var totals = ImpactScoreCalculator.ComputeTotals(processes);
        var top5 = processes
            .Where(p => p.Pid > 4)   // skip idle / system
            .Select(p => new ProcessImpact
            {
                Pid         = p.Pid,
                Name        = p.Name,
                ImpactScore = ImpactScoreCalculator.Calculate(p, totals),
                CpuPercent  = p.CpuPercent,
                MemoryBytes = p.WorkingSetBytes,
            })
            .OrderByDescending(x => x.ImpactScore)
            .Take(5)
            .ToList();

        // ── Active automation count ───────────────────────────────────────────
        var automations = 0;
        if (_settings.ProBalanceEnabled)   automations++;
        if (_settings.GamingModeEnabled)   automations++;
        if (_settings.Rules.Count > 0)     automations++;
        if (_settings.AlertRules.Count > 0) automations++;

        // ── Bottleneck analysis ───────────────────────────────────────────────
        var bottleneck = BottleneckDetector.Analyse(m, processes);

        // ── Build snapshot ─────────────────────────────────────────────────────
        var snapshot = new SystemHealthSnapshot
        {
            OverallHealth   = HealthScoring.ScoreToLevel(overall),
            OverallScore    = overall,
            OverallTrend    = HealthScoring.ComputeTrend(_overallHistory.ToList()),
            Cpu = new SubsystemHealth
            {
                Name         = "CPU",
                Score        = cpuScore,
                Level        = HealthScoring.ScoreToLevel(cpuScore),
                Trend        = HealthScoring.ComputeTrend(_cpuHistory.ToList()),
                CurrentValue = cpuVal,
                Summary      = $"{cpuVal:F0}% used",
            },
            Memory = new SubsystemHealth
            {
                Name         = "Memory",
                Score        = memScore,
                Level        = HealthScoring.ScoreToLevel(memScore),
                Trend        = HealthScoring.ComputeTrend(_memHistory.ToList()),
                CurrentValue = memVal,
                Summary      = $"{memVal:F0}% used ({FormatBytes(m.Memory.UsedBytes)} / {FormatBytes(m.Memory.TotalBytes)})",
            },
            Disk = new SubsystemHealth
            {
                Name         = "Disk",
                Score        = diskScore,
                Level        = HealthScoring.ScoreToLevel(diskScore),
                Trend        = HealthScoring.ComputeTrend(_diskHistory.ToList()),
                CurrentValue = diskUsed,
                Summary      = m.Disks.Count > 0 ? $"{diskVal:F0}% active · {diskUsed:F0}% full" : "No disks",
            },
            Gpu = new SubsystemHealth
            {
                Name         = "GPU",
                Score        = gpuScore,
                Level        = HealthScoring.ScoreToLevel(gpuScore),
                Trend        = HealthScoring.ComputeTrend(_gpuHistory.ToList()),
                CurrentValue = gpuVal,
                Summary      = m.Gpus.Count > 0 ? $"{gpuVal:F0}% used" : "No GPU data",
            },
            TopConsumers      = top5,
            ActiveAutomations = automations,
            Bottleneck        = bottleneck,
            Timestamp         = DateTime.UtcNow,
        };

        _subject.OnNext(snapshot);
    }

    private static void Enqueue(Queue<double> queue, double value)
    {
        if (queue.Count >= HistorySize) queue.Dequeue();
        queue.Enqueue(value);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F0} MB";
        return $"{bytes / 1024.0:F0} KB";
    }

    public void Dispose()
    {
        Stop();
        _subject.Dispose();
    }
}
