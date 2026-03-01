using System.Reactive.Subjects;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Storage;

/// <summary>
/// Background service that continuously monitors live metric streams,
/// detects anomalies via statistical analysis and hard thresholds,
/// and writes detected events to the events table via <see cref="IEventWriter"/>.
///
/// Lifecycle: Start() / Stop() / IDisposable — follows AlertsService pattern.
/// </summary>
public sealed class AnomalyDetectionService : IDisposable
{
    private readonly AnomalyDetectionConfig      _config;
    private readonly IEventWriter                _writer;
    private readonly ISystemMetricsProvider      _metricsProvider;
    private readonly IProcessProvider            _processProvider;
    private readonly INetworkConnectionsProvider _networkProvider;

    // ── Statistical sliding windows ────────────────────────────────────────
    private readonly SlidingStats _cpuStats = new(60);
    private readonly SlidingStats _memStats = new(60);
    private readonly SlidingStats _gpuStats = new(60);
    private readonly SlidingStats _netStats = new(60);

    // Per-process windows (keyed by process name)
    private readonly Dictionary<string, SlidingStats> _procStats = new();

    // ── New-connection tracking ────────────────────────────────────────────
    private readonly HashSet<string> _seenEndpoints = new();
    private readonly DateTime        _startupUtc    = DateTime.UtcNow;

    // ── Cooldown tracking: tuple key avoids string interpolation allocation per call ──
    private readonly Dictionary<(string EventType, string MetricName), DateTime> _lastFired = new();

    // ── Subscriptions ──────────────────────────────────────────────────────
    private IDisposable? _metricsSub;
    private IDisposable? _processSub;
    private IDisposable? _networkSub;

    // ── Observable output ──────────────────────────────────────────────────
    private readonly Subject<StoredEvent> _anomalyDetected = new();

    /// <summary>Hot observable that fires each time an anomaly is written to the events table.</summary>
    public IObservable<StoredEvent> AnomalyDetected => _anomalyDetected;

    private bool _disposed;

    public AnomalyDetectionService(
        AnomalyDetectionConfig      config,
        IEventWriter                writer,
        ISystemMetricsProvider      metricsProvider,
        IProcessProvider            processProvider,
        INetworkConnectionsProvider networkProvider)
    {
        _config          = config;
        _writer          = writer;
        _metricsProvider = metricsProvider;
        _processProvider = processProvider;
        _networkProvider = networkProvider;
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    public void Start()
    {
        if (!_config.Enabled) return;

        _metricsSub = _metricsProvider
            .GetMetricsStream(TimeSpan.FromSeconds(2))
            .Subscribe(OnMetricsTick, _ => { });

        _processSub = _processProvider
            .GetProcessStream(TimeSpan.FromSeconds(2))
            .Subscribe(OnProcessTick, _ => { });

        _networkSub = _networkProvider
            .GetConnectionStream(TimeSpan.FromSeconds(5))
            .Subscribe(OnNetworkTick, _ => { });
    }

    public void Stop()
    {
        _metricsSub?.Dispose();
        _processSub?.Dispose();
        _networkSub?.Dispose();
        _metricsSub = _processSub = _networkSub = null;
    }

    // ── Metrics tick ───────────────────────────────────────────────────────

    private void OnMetricsTick(SystemMetrics m)
    {
        double cpu = m.Cpu.TotalPercent;
        double mem = m.Memory.TotalBytes > 0
            ? m.Memory.UsedBytes * 100.0 / m.Memory.TotalBytes
            : 0;
        double gpu = m.Gpus.Count > 0 ? m.Gpus[0].UsagePercent : -1;

        // Check anomalies BEFORE pushing (compare new value against existing baseline)
        CheckMetric(EventType.CpuHigh, "cpu", cpu, _cpuStats, _config.SigmaCpu, _config.CpuCeiling);
        CheckMetric(EventType.MemHigh, "mem", mem, _memStats, _config.SigmaMem, _config.MemCeiling);
        if (gpu >= 0)
            CheckMetric(EventType.GpuHigh, "gpu", gpu, _gpuStats, _config.SigmaGpu, _config.GpuCeiling);

        _cpuStats.Push(cpu);
        _memStats.Push(mem);
        if (gpu >= 0) _gpuStats.Push(gpu);
    }

    private void CheckMetric(string eventType, string metricName, double value,
        SlidingStats stats, double kSigma, double ceiling)
    {
        bool hitCeiling   = value >= ceiling;
        bool hitSigma     = stats.IsAnomaly(value, kSigma);
        bool hitHighSigma = stats.IsAnomaly(value, kSigma * 1.5);

        if (!hitCeiling && !hitSigma) return;
        if (!IsCooldownElapsed(eventType, metricName)) return;

        int severity = hitCeiling
            ? EventSeverity.Critical
            : hitHighSigma
                ? EventSeverity.Warning
                : EventSeverity.Info;

        string desc = hitCeiling
            ? $"{metricName.ToUpperInvariant()} hit ceiling: {value:F1}% ≥ {ceiling:F0}%"
            : $"{metricName.ToUpperInvariant()} statistical spike: {value:F1}% (σ threshold)";

        MarkCooldown(eventType, metricName);
        _ = FireEvent(eventType, severity, metricName, value,
            hitCeiling ? ceiling : null, desc);
    }

    // ── Process tick ───────────────────────────────────────────────────────

    private void OnProcessTick(IReadOnlyList<ProcessInfo> procs)
    {
        foreach (var p in procs)
        {
            if (!_procStats.TryGetValue(p.Name, out var stats))
            {
                stats = new SlidingStats(_config.WindowSize);
                _procStats[p.Name] = stats;
            }

            double cpu      = p.CpuPercent;
            bool isAnomaly  = stats.IsAnomaly(cpu, _config.SigmaProcess);
            double baseline = stats.Mean();

            stats.Push(cpu);   // push AFTER checking

            if (!isAnomaly) continue;
            if (cpu - baseline < _config.ProcessSpikeMinDeltaCpu) continue;
            if (!IsCooldownElapsed(EventType.ProcessSpike, p.Name)) continue;

            MarkCooldown(EventType.ProcessSpike, p.Name);

            string desc = $"Process spike: {p.Name} CPU {cpu:F1}% " +
                          $"(baseline ~{baseline:F1}%)";
            _ = FireEvent(EventType.ProcessSpike, EventSeverity.Warning,
                p.Name, cpu, baseline + _config.ProcessSpikeMinDeltaCpu, desc,
                $"{{\"pid\":{p.Pid}}}");
        }

        // Trim procStats if it grows unbounded (long-lived processes accumulate)
        if (_procStats.Count > 500)
        {
            var toDrop = _procStats.Keys.Take(100).ToList();
            foreach (var k in toDrop) _procStats.Remove(k);
        }
    }

    // ── Network tick ───────────────────────────────────────────────────────

    private void OnNetworkTick(IReadOnlyList<NetworkConnection> conns)
    {
        // ── Aggregate bandwidth anomaly ──────────────────────────────────
        double totalBps   = conns.Sum(c => c.SendBytesPerSec + c.RecvBytesPerSec);
        bool isNetAnomaly = _netStats.IsAnomaly(totalBps, _config.SigmaNet);
        double netBaseline = _netStats.Mean();
        _netStats.Push(totalBps);

        if (isNetAnomaly && IsCooldownElapsed(EventType.NetAnomaly, "net_bw"))
        {
            MarkCooldown(EventType.NetAnomaly, "net_bw");
            string desc = $"Network bandwidth spike: {totalBps / 1_048_576.0:F1} MB/s " +
                          $"(baseline ~{netBaseline / 1_048_576.0:F1} MB/s)";
            _ = FireEvent(EventType.NetAnomaly, EventSeverity.Warning,
                "net_bw", totalBps, netBaseline, desc);
        }

        // ── New-connection detection ─────────────────────────────────────
        bool inGrace = (DateTime.UtcNow - _startupUtc).TotalSeconds
                       < _config.NewConnectionGracePeriodSeconds;

        if (inGrace)
        {
            // Seed seen-endpoints during grace period without firing events
            foreach (var c in conns)
            {
                if (c.State != TcpConnectionState.Established) continue;
                if (string.IsNullOrEmpty(c.RemoteAddress)) continue;
                if (_seenEndpoints.Count < _config.NewConnectionMaxTracked)
                    _seenEndpoints.Add($"{c.RemoteAddress}:{c.RemotePort}");
            }
            return;
        }

        foreach (var c in conns)
        {
            if (c.State != TcpConnectionState.Established) continue;
            if (string.IsNullOrEmpty(c.RemoteAddress) || c.RemoteAddress == "0.0.0.0") continue;

            var endpointKey = $"{c.RemoteAddress}:{c.RemotePort}";
            if (_seenEndpoints.Contains(endpointKey)) continue;

            if (_seenEndpoints.Count < _config.NewConnectionMaxTracked)
                _seenEndpoints.Add(endpointKey);

            if (!IsCooldownElapsed(EventType.NewConnection, endpointKey)) continue;
            MarkCooldown(EventType.NewConnection, endpointKey);

            string name = string.IsNullOrEmpty(c.ProcessName)
                ? string.Empty
                : $" ({c.ProcessName})";
            string desc = $"New connection: {c.RemoteAddress}:{c.RemotePort}{name}";
            _ = FireEvent(EventType.NewConnection, EventSeverity.Info,
                "net_conn", c.RemotePort, null, desc,
                $"{{\"remote_addr\":\"{c.RemoteAddress}\",\"remote_port\":{c.RemotePort},\"pid\":{c.ProcessId}}}");
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private bool IsCooldownElapsed(string eventType, string metricName)
        => !_lastFired.TryGetValue((eventType, metricName), out var last)
           || (DateTime.UtcNow - last).TotalSeconds >= _config.CooldownSeconds;

    private void MarkCooldown(string eventType, string metricName)
        => _lastFired[(eventType, metricName)] = DateTime.UtcNow;

    private async Task FireEvent(
        string  eventType, int     severity,
        string? metricName, double? metricValue, double? threshold,
        string? description, string? metadataJson = null)
    {
        try
        {
            await _writer.InsertEventAsync(
                eventType, severity,
                metricName, metricValue, threshold, description, metadataJson);

            var stored = new StoredEvent(
                Id:           0,
                Timestamp:    DateTimeOffset.UtcNow,
                EventType:    eventType,
                Severity:     severity,
                MetricName:   metricName,
                MetricValue:  metricValue,
                Threshold:    threshold,
                Description:  description,
                MetadataJson: metadataJson);

            _anomalyDetected.OnNext(stored);
        }
        catch { /* swallow — never crash the monitoring loop */ }
    }

    // ── IDisposable ────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _anomalyDetected.Dispose();
    }
}
