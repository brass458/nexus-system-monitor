using System.Reactive.Linq;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Automation;

/// <summary>
/// Automatically trims process working sets on a configurable interval.
/// Trims all background processes when RAM pressure is high, or only idle
/// large processes when RAM is plentiful.
/// </summary>
public sealed class SmartTrimService : IDisposable
{
    private readonly IProcessProvider         _processProvider;
    private readonly IForegroundWindowProvider _foregroundWindow;
    private readonly ISystemMetricsProvider   _metricsProvider;
    private readonly AppSettings              _settings;

    // pid → last trim time (per-PID cooldown)
    private readonly Dictionary<int, DateTime> _lastTrimTime = new();
    // pid → consecutive idle tick count (reused from interval loop)
    private readonly Dictionary<int, int> _idleTicks = new();

    private IDisposable? _timerSubscription;
    private IDisposable? _tickSubscription;
    private bool _running;

    private const int IdleTicksForTrim = 3;

    public SmartTrimService(
        IProcessProvider          processProvider,
        IForegroundWindowProvider foregroundWindow,
        ISystemMetricsProvider    metricsProvider,
        AppSettings               settings)
    {
        _processProvider  = processProvider;
        _foregroundWindow = foregroundWindow;
        _metricsProvider  = metricsProvider;
        _settings         = settings;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;

        // Track idle ticks on every process-stream tick (2s)
        _tickSubscription = _processProvider
            .GetProcessStream(TimeSpan.FromSeconds(2))
            .Subscribe(TrackIdleTicks, _ => { });

        // Actual trim runs on the configured interval
        ScheduleNextTrim();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _timerSubscription?.Dispose();
        _timerSubscription = null;
        _tickSubscription?.Dispose();
        _tickSubscription = null;
    }

    private void ScheduleNextTrim()
    {
        if (!_running) return;
        var interval = TimeSpan.FromSeconds(
            Math.Max(10, _settings.SmartTrimIntervalSeconds));
        _timerSubscription = Observable.Timer(interval)
            .Subscribe(__ =>
            {
                var _ = RunTrimAsync();
                ScheduleNextTrim();
            });
    }

    private void TrackIdleTicks(IReadOnlyList<ProcessInfo> processes)
    {
        var alive = new HashSet<int>(processes.Select(p => p.Pid));
        foreach (var pid in _idleTicks.Keys.Where(k => !alive.Contains(k)).ToList())
            _idleTicks.Remove(pid);

        foreach (var proc in processes)
        {
            if (proc.CpuPercent < 1.0)
            {
                _idleTicks.TryGetValue(proc.Pid, out var t);
                _idleTicks[proc.Pid] = t + 1;
            }
            else
            {
                _idleTicks[proc.Pid] = 0;
            }
        }
    }

    private async Task RunTrimAsync()
    {
        if (!_settings.SmartTrimEnabled) return;

        var processes = await _processProvider.GetProcessesAsync();
        var metrics   = await _metricsProvider.GetMetricsAsync();

        double availPct = 100.0 - metrics.Memory.UsedPercent;
        bool   highPressure = availPct < _settings.SmartTrimPressurePercent;

        int fgPid  = _foregroundWindow.GetForegroundProcessId();
        long minWs = (long)_settings.SmartTrimMinWorkingSetMB * 1024 * 1024;
        var now    = DateTime.UtcNow;

        foreach (var proc in processes)
        {
            if (proc.Pid == fgPid)             continue;
            if (proc.Pid == Environment.ProcessId) continue;
            if (proc.Category == ProcessCategory.SystemKernel) continue;

            // Per-PID cooldown (120 seconds)
            if (_lastTrimTime.TryGetValue(proc.Pid, out var last) &&
                (now - last).TotalSeconds < 120)
                continue;

            bool shouldTrim;
            if (highPressure)
            {
                // Under pressure: trim anything > 50 MB
                shouldTrim = proc.WorkingSetBytes > 50L * 1024 * 1024;
            }
            else
            {
                // Normal: only trim large idle processes
                _idleTicks.TryGetValue(proc.Pid, out var ticks);
                shouldTrim = proc.WorkingSetBytes > minWs && ticks >= IdleTicksForTrim;
            }

            if (shouldTrim)
            {
                try
                {
                    await _processProvider.TrimWorkingSetAsync(proc.Pid);
                    _lastTrimTime[proc.Pid] = now;
                }
                catch { }
            }
        }

        // Evict stale entries
        foreach (var pid in _lastTrimTime.Keys
            .Where(k => !processes.Any(p => p.Pid == k)).ToList())
            _lastTrimTime.Remove(pid);
    }

    public void Dispose() => Stop();
}
