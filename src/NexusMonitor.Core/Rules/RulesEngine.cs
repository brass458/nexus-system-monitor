using System.Reactive.Linq;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;
using NexusMonitor.Core.Storage;

namespace NexusMonitor.Core.Rules;

/// <summary>
/// Background service that applies persistent ProcessRules to newly-launched
/// processes and evaluates watchdog conditions on every process snapshot.
/// Also applies ProcessPreference settings to new processes (preferences are
/// lower priority than explicit rules — rules win when both match).
/// </summary>
public sealed class RulesEngine : IDisposable
{
    private readonly IProcessProvider        _processProvider;
    private readonly AppSettings             _settings;
    private readonly ProcessPreferenceStore? _preferenceStore;
    private readonly HashSet<int> _seenPids = new();
    // watchdog tracking: (pid, ruleId) → first-seen-over-threshold time
    private readonly Dictionary<(int pid, Guid ruleId), DateTime> _conditionFirstSeen = new();
    private IDisposable? _subscription;

    // Cached enabled-rules list — rebuilt only when _settings.Rules reference changes
    private List<ProcessRule>? _cachedRules;
    private IReadOnlyList<ProcessRule>? _cachedRulesSource;

    // KeepRunning state: normalized exe name → state record
    private sealed class KeepRunningState
    {
        public string?   ImagePath    { get; set; }
        public string?   CommandLine  { get; set; }
        public DateTime  LastExitTime { get; set; }
        public int       RestartCount { get; set; }
        public DateTime  WindowStart  { get; set; } = DateTime.UtcNow;
    }
    private readonly Dictionary<string, KeepRunningState> _keepRunningState = new();

    public RulesEngine(IProcessProvider processProvider, AppSettings settings,
        ProcessPreferenceStore? preferenceStore = null)
    {
        _processProvider  = processProvider;
        _settings         = settings;
        _preferenceStore  = preferenceStore;
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
        // Rebuild cached enabled-rules list only when the source collection reference changes.
        var srcRules = _settings.Rules;
        if (!ReferenceEquals(srcRules, _cachedRulesSource))
        {
            _cachedRules       = srcRules?.Where(r => r.IsEnabled).ToList();
            _cachedRulesSource = srcRules;
        }
        var rules = _cachedRules;
        if (rules is null || rules.Count == 0) return;

        var alive = new HashSet<int>(processes.Select(p => p.Pid));

        foreach (var proc in processes)
        {
            bool isNew = _seenPids.Add(proc.Pid);
            bool ruleMatched = false;
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
                    ruleMatched = true;
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
                    if (rule.CpuSetIds is { Length: > 0 })
                        try { await _processProvider.SetCpuSetsAsync(proc.Pid, rule.CpuSetIds); } catch { }
                }

                // ── KeepRunning: record image path while alive ─────────────
                if (rule.KeepRunning)
                {
                    var key = rule.ProcessNamePattern.ToLowerInvariant();
                    if (!_keepRunningState.TryGetValue(key, out var ks))
                        _keepRunningState[key] = ks = new KeepRunningState();
                    // Update image path from the running process snapshot
                    if (!string.IsNullOrEmpty(proc.ImagePath))
                        ks.ImagePath = proc.ImagePath;
                    ks.RestartCount = 0; // process is alive — reset counter
                }

                // ── Watchdog conditions ────────────────────────────────────
                if (rule.WatchdogAction != WatchdogAction.None && rule.Condition is not null)
                    await EvaluateWatchdog(proc, rule);
            }

            // ── Apply saved preferences if no explicit rule matched ────────
            if (isNew && !ruleMatched && _preferenceStore is not null)
            {
                var pref = _preferenceStore.Get(proc.Name);
                if (pref is not null)
                {
                    if (pref.Priority.HasValue)
                        try { await _processProvider.SetPriorityAsync(proc.Pid, pref.Priority.Value); } catch { }
                    if (pref.AffinityMask.HasValue)
                        try { await _processProvider.SetAffinityAsync(proc.Pid, pref.AffinityMask.Value); } catch { }
                    if (pref.IoPriority.HasValue)
                        try { await _processProvider.SetIoPriorityAsync(proc.Pid, pref.IoPriority.Value); } catch { }
                    if (pref.MemoryPriority.HasValue)
                        try { await _processProvider.SetMemoryPriorityAsync(proc.Pid, pref.MemoryPriority.Value); } catch { }
                    if (pref.EfficiencyMode.HasValue)
                        try { await _processProvider.SetEfficiencyModeAsync(proc.Pid, pref.EfficiencyMode.Value); } catch { }
                }
            }
        }

        // ── Evict dead PIDs ────────────────────────────────────────────────
        var deadPids = _seenPids.Where(pid => !alive.Contains(pid)).ToList();
        foreach (var dead in deadPids)
        {
            _seenPids.Remove(dead);
            foreach (var key in _conditionFirstSeen.Keys.Where(k => k.pid == dead).ToList())
                _conditionFirstSeen.Remove(key);
        }

        // ── KeepRunning: detect exits and restart ─────────────────────────
        await EvaluateKeepRunning(processes, rules);

        // ── Instance count limits ─────────────────────────────────────────
        await EvaluateInstanceLimits(processes, rules);
    }

    // ── KeepRunning ──────────────────────────────────────────────────────────

    private Task EvaluateKeepRunning(
        IReadOnlyList<ProcessInfo> processes,
        List<ProcessRule> rules)
    {
        var now = DateTime.UtcNow;
        var aliveNames = new HashSet<string>(
            processes.Select(p => p.Name.ToLowerInvariant()));

        foreach (var rule in rules.Where(r => r.KeepRunning))
        {
            var key = rule.ProcessNamePattern.ToLowerInvariant();
            if (!_keepRunningState.TryGetValue(key, out var ks)) continue;

            // Check if process has exited (we had image path, but it's no longer running)
            bool isAlive = aliveNames.Any(n =>
                rule.Matches(n) || n.Contains(key.TrimEnd('*').TrimStart('*'),
                    StringComparison.OrdinalIgnoreCase));

            if (isAlive || string.IsNullOrEmpty(ks.ImagePath)) continue;

            // Process exited — record exit time on first observation
            if (ks.LastExitTime == default)
            {
                ks.LastExitTime = now;
                continue;
            }

            // Cooldown check
            var cooldown = TimeSpan.FromSeconds(
                Math.Max(1, rule.KeepRunningCooldownSeconds));
            if ((now - ks.LastExitTime) < cooldown) continue;

            // Retry cap: max N restarts per 60-second window
            if ((now - ks.WindowStart).TotalSeconds > 60)
            {
                ks.WindowStart  = now;
                ks.RestartCount = 0;
            }
            if (ks.RestartCount >= rule.KeepRunningMaxRetries) continue;

            // Restart
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = ks.ImagePath,
                    UseShellExecute = true
                });
                ks.RestartCount++;
                ks.LastExitTime = default;
            }
            catch { /* best-effort */ }
        }

        return Task.CompletedTask;
    }

    // ── Instance count limits ─────────────────────────────────────────────────

    private async Task EvaluateInstanceLimits(
        IReadOnlyList<ProcessInfo> processes,
        List<ProcessRule> rules)
    {
        foreach (var rule in rules.Where(r => r.MaxInstances.HasValue))
        {
            var max = rule.MaxInstances!.Value;
            var matching = processes
                .Where(p => rule.Matches(p.Name))
                .OrderBy(p => p.StartTime)    // oldest first → kill newest excess
                .ToList();

            if (matching.Count <= max) continue;

            var excess = matching.Skip(max).ToList();
            foreach (var proc in excess)
            {
                try { await _processProvider.KillProcessAsync(proc.Pid); } catch { }
            }
        }
    }

    // ── Watchdog ─────────────────────────────────────────────────────────────

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
                    case WatchdogAction.SetIoPriorityLow:
                        await _processProvider.SetIoPriorityAsync(proc.Pid, IoPriority.Low);
                        break;
                    case WatchdogAction.SetEfficiencyMode:
                        await _processProvider.SetEfficiencyModeAsync(proc.Pid, true);
                        break;
                    case WatchdogAction.TrimWorkingSet:
                        await _processProvider.TrimWorkingSetAsync(proc.Pid);
                        break;
                    case WatchdogAction.Restart:
                        await _processProvider.KillProcessAsync(proc.Pid);
                        // Process restart (best-effort via image path if available)
                        if (!string.IsNullOrEmpty(proc.ImagePath))
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName        = proc.ImagePath,
                                    UseShellExecute = true
                                });
                            }
                            catch { }
                        }
                        break;
                    case WatchdogAction.ReduceAffinity:
                        await ApplyReduceAffinityAsync(proc.Pid, rule.ActionParams?.ReduceCoreCount ?? 1);
                        break;
                    case WatchdogAction.LogOnly:
                        // No action — the condition-sustained log is implicit
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

    private async Task ApplyReduceAffinityAsync(int pid, int reduceCores)
    {
        try
        {
            var (procMask, sysMask) = await _processProvider.GetAffinityMasksAsync(pid);
            int coreCount = CountBits(procMask);
            int newCount  = Math.Max(1, coreCount - reduceCores);
            long newMask  = BuildMaskWithNCores(newCount, sysMask);
            await _processProvider.SetAffinityAsync(pid, newMask);
        }
        catch { }
    }

    private static int CountBits(long mask)
    {
        int count = 0;
        while (mask != 0) { count += (int)(mask & 1); mask >>= 1; }
        return count;
    }

    private static long BuildMaskWithNCores(int n, long sysMask)
    {
        long result = 0;
        int  added  = 0;
        for (int i = 0; i < 64 && added < n; i++)
        {
            if ((sysMask & (1L << i)) != 0) { result |= (1L << i); added++; }
        }
        return result == 0 ? 1 : result;
    }

    public void Dispose() => Stop();
}
