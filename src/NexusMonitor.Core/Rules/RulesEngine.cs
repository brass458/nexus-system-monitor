using System.Reactive.Linq;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Rules;

/// <summary>
/// Background service that applies persistent ProcessRules to newly-launched
/// processes and evaluates watchdog conditions on every process snapshot.
/// </summary>
public sealed class RulesEngine : IDisposable
{
    private readonly IProcessProvider _processProvider;
    private readonly AppSettings _settings;
    private readonly HashSet<int> _seenPids = new();
    // watchdog tracking: (pid, ruleId) → first-seen-over-threshold time
    private readonly Dictionary<(int pid, Guid ruleId), DateTime> _conditionFirstSeen = new();
    private IDisposable? _subscription;

    public RulesEngine(IProcessProvider processProvider, AppSettings settings)
    {
        _processProvider = processProvider;
        _settings = settings;
    }

    public void Start()
    {
        _subscription = _processProvider
            .GetProcessStream(TimeSpan.FromSeconds(2))
            .Subscribe(OnTick, _ => { });
    }

    public void Stop() { _subscription?.Dispose(); _subscription = null; }

    private async void OnTick(IReadOnlyList<ProcessInfo> processes)
    {
        var rules = _settings.Rules?.Where(r => r.IsEnabled).ToList();
        if (rules is null || rules.Count == 0) return;

        var alive = new HashSet<int>(processes.Select(p => p.Pid));

        foreach (var proc in processes)
        {
            bool isNew = _seenPids.Add(proc.Pid);
            foreach (var rule in rules.Where(r => r.Matches(proc.Name)))
            {
                // ── Disallowed: terminate immediately ─────────────────────
                if (rule.Disallowed && isNew)
                {
                    try { await _processProvider.KillProcessAsync(proc.Pid); } catch { }
                    continue;
                }

                // ── Persistent actions on new process ─────────────────────
                if (isNew)
                {
                    if (rule.Priority.HasValue)
                        try { await _processProvider.SetPriorityAsync(proc.Pid, rule.Priority.Value); } catch { }
                    if (rule.AffinityMask.HasValue)
                        try { await _processProvider.SetAffinityAsync(proc.Pid, rule.AffinityMask.Value); } catch { }
                    if (rule.IoPriority.HasValue)
                        try { await _processProvider.SetIoPriorityAsync(proc.Pid, rule.IoPriority.Value); } catch { }
                    if (rule.MemoryPriority.HasValue)
                        try { await _processProvider.SetMemoryPriorityAsync(proc.Pid, rule.MemoryPriority.Value); } catch { }
                    if (rule.EfficiencyMode.HasValue)
                        try { await _processProvider.SetEfficiencyModeAsync(proc.Pid, rule.EfficiencyMode.Value); } catch { }
                }

                // ── Watchdog conditions ────────────────────────────────────
                if (rule.WatchdogAction != WatchdogAction.None && rule.Condition is not null)
                    await EvaluateWatchdog(proc, rule);
            }
        }

        // Evict dead PIDs
        foreach (var dead in _seenPids.Where(pid => !alive.Contains(pid)).ToList())
        {
            _seenPids.Remove(dead);
            foreach (var key in _conditionFirstSeen.Keys.Where(k => k.pid == dead).ToList())
                _conditionFirstSeen.Remove(key);
        }
    }

    private async Task EvaluateWatchdog(ProcessInfo proc, ProcessRule rule)
    {
        var cond = rule.Condition!;
        bool over = cond.Type switch
        {
            ConditionType.CpuAbove => proc.CpuPercent > cond.CpuThresholdPercent,
            ConditionType.RamAbove => proc.WorkingSetBytes > cond.RamThresholdBytes,
            _                      => true
        };

        var key = (proc.Pid, rule.Id);
        if (over)
        {
            var now = DateTime.UtcNow;
            if (!_conditionFirstSeen.TryGetValue(key, out var first))
            {
                _conditionFirstSeen[key] = now;
                return;
            }
            if ((now - first).TotalSeconds < cond.DurationSeconds) return;

            // Condition sustained long enough — fire action
            _conditionFirstSeen.Remove(key);
            try
            {
                switch (rule.WatchdogAction)
                {
                    case WatchdogAction.SetBelowNormal:
                        await _processProvider.SetPriorityAsync(proc.Pid, ProcessPriority.BelowNormal);
                        break;
                    case WatchdogAction.SetIdle:
                        await _processProvider.SetPriorityAsync(proc.Pid, ProcessPriority.Idle);
                        break;
                    case WatchdogAction.Terminate:
                        await _processProvider.KillProcessAsync(proc.Pid);
                        break;
                }
            }
            catch { /* process may have exited */ }
        }
        else
        {
            _conditionFirstSeen.Remove(key);
        }
    }

    public void Dispose() => Stop();
}
