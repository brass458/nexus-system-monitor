using System.Reactive.Linq;
using System.Reactive.Subjects;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Automation;

/// <summary>
/// Background daemon that dynamically lowers CPU-hogging background processes
/// to restore system responsiveness, then restores them when load drops.
/// Inspired by Process Lasso's ProBalance algorithm.
/// </summary>
public sealed class ProBalanceService : IDisposable
{
    private readonly IProcessProvider _processProvider;
    private readonly IForegroundWindowProvider _foregroundWindow;
    private readonly AppSettings _settings; // live reference — reads current options each tick

    // pid → original priority (before we throttled them)
    private readonly Dictionary<int, ProcessPriority> _throttled = new();
    private readonly Subject<ProBalanceEvent> _events = new();
    private IDisposable? _subscription;
    private bool _running;

    public IObservable<ProBalanceEvent> Events => _events.AsObservable();
    public bool IsRunning => _running;

    public ProBalanceService(
        IProcessProvider processProvider,
        IForegroundWindowProvider foregroundWindow,
        AppSettings settings)
    {
        _processProvider = processProvider;
        _foregroundWindow = foregroundWindow;
        _settings = settings;
    }

    /// <summary>Start the monitoring loop. Safe to call multiple times.</summary>
    public void Start()
    {
        if (_running) return;
        _running = true;
        _subscription = Observable
            .Timer(TimeSpan.Zero, TimeSpan.FromMilliseconds(500))
            .SelectMany(_ => Observable.FromAsync(ct => _processProvider.GetProcessesAsync(ct)))
            .Subscribe(OnTick, ex => { /* swallow — loop must not crash */ });
        _events.OnNext(new ProBalanceEvent(
            ProBalanceEventType.Started, 0, string.Empty,
            ProcessPriority.Normal, ProcessPriority.Normal, DateTime.UtcNow));
    }

    /// <summary>Stop the monitoring loop and restore all throttled processes.</summary>
    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _subscription?.Dispose();
        _subscription = null;
        _ = RestoreAllAsync();
        _events.OnNext(new ProBalanceEvent(
            ProBalanceEventType.Stopped, 0, string.Empty,
            ProcessPriority.Normal, ProcessPriority.Normal, DateTime.UtcNow));
    }

    private async void OnTick(IReadOnlyList<ProcessInfo> processes)
    {
        if (!_settings.ProBalanceEnabled) return;

        double totalCpu = processes.Sum(p => p.CpuPercent);
        double threshold = _settings.ProBalanceCpuThreshold;
        int fgPid = _foregroundWindow.GetForegroundProcessId();
        var exclusions = _settings.ProBalanceExclusions;

        if (totalCpu >= threshold)
        {
            // Identify background hogs to throttle
            var candidates = processes
                .Where(p =>
                    p.CpuPercent > 5.0 &&
                    p.Pid != fgPid &&
                    p.Pid != Environment.ProcessId &&
                    p.Category != ProcessCategory.SystemKernel &&
                    !_throttled.ContainsKey(p.Pid) &&
                    !exclusions.Any(ex => p.Name.Equals(ex, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(p => p.CpuPercent)
                .Take(5);

            foreach (var proc in candidates)
            {
                // Infer original priority from what the OS is running it at
                var original = InferPriority(proc);
                if (original <= ProcessPriority.BelowNormal) continue; // already low
                try
                {
                    await _processProvider.SetPriorityAsync(proc.Pid, ProcessPriority.BelowNormal);
                    _throttled[proc.Pid] = original;
                    _events.OnNext(new ProBalanceEvent(
                        ProBalanceEventType.Throttled, proc.Pid, proc.Name,
                        original, ProcessPriority.BelowNormal, DateTime.UtcNow));
                }
                catch { /* process may have exited */ }
            }
        }
        else if (totalCpu < threshold * 0.7) // restore at 70% of threshold to avoid oscillation
        {
            // Restore all throttled processes
            var toRestore = _throttled.ToList();
            foreach (var (pid, original) in toRestore)
            {
                try
                {
                    await _processProvider.SetPriorityAsync(pid, original);
                    _throttled.Remove(pid);
                    var name = processes.FirstOrDefault(p => p.Pid == pid)?.Name ?? $"PID {pid}";
                    _events.OnNext(new ProBalanceEvent(
                        ProBalanceEventType.Restored, pid, name,
                        ProcessPriority.BelowNormal, original, DateTime.UtcNow));
                }
                catch { _throttled.Remove(pid); /* process exited — just remove */ }
            }
        }
        else
        {
            // Mid-zone: only restore processes that are no longer using CPU
            var inactive = _throttled.Keys
                .Where(pid => processes.All(p => p.Pid != pid || p.CpuPercent < 2.0))
                .ToList();
            foreach (var pid in inactive)
            {
                var original = _throttled[pid];
                try { await _processProvider.SetPriorityAsync(pid, original); } catch { }
                _throttled.Remove(pid);
                var name = processes.FirstOrDefault(p => p.Pid == pid)?.Name ?? $"PID {pid}";
                _events.OnNext(new ProBalanceEvent(
                    ProBalanceEventType.Restored, pid, name,
                    ProcessPriority.BelowNormal, original, DateTime.UtcNow));
            }
        }

        // Clean up entries for processes that have exited
        var alive = new HashSet<int>(processes.Select(p => p.Pid));
        foreach (var pid in _throttled.Keys.Where(k => !alive.Contains(k)).ToList())
            _throttled.Remove(pid);
    }

    private static ProcessPriority InferPriority(ProcessInfo p)
    {
        // Without reading the actual priority class from Windows, assume Normal for user apps
        return ProcessPriority.Normal;
    }

    private async Task RestoreAllAsync()
    {
        foreach (var (pid, original) in _throttled.ToList())
        {
            try { await _processProvider.SetPriorityAsync(pid, original); } catch { }
        }
        _throttled.Clear();
    }

    public void Dispose()
    {
        Stop();
        _events.Dispose();
    }
}
