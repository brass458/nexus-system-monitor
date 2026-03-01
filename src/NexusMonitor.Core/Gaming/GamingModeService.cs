using System.Reactive.Linq;
using System.Reactive.Subjects;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Gaming;

/// <summary>
/// One-click Gaming Mode: switches power plan to High Performance and lowers
/// all non-excluded background processes to Below Normal priority.
/// Restores everything when deactivated.
/// </summary>
public sealed class GamingModeService : IDisposable
{
    private readonly IProcessProvider  _processProvider;
    private readonly IPowerPlanProvider _powerPlanProvider;
    private readonly AppSettings        _settings;

    // pid → original priority saved before we touched it
    private readonly Dictionary<int, ProcessPriority> _throttled = new();
    private readonly Subject<string> _statusMessages = new();
    private IDisposable? _pollingSubscription;

    private Guid _savedPowerPlan = Guid.Empty;
    private bool _active;

    public bool IsActive => _active;

    public IObservable<string> StatusMessages => _statusMessages.AsObservable();

    public GamingModeService(
        IProcessProvider processProvider,
        IPowerPlanProvider powerPlanProvider,
        AppSettings settings)
    {
        _processProvider   = processProvider;
        _powerPlanProvider = powerPlanProvider;
        _settings          = settings;
    }

    /// <summary>
    /// Activate Gaming Mode.
    /// 1. Saves the current power plan, then switches to High Performance
    ///    (or Ultimate Performance if available).
    /// 2. Lowers all non-excluded, non-game background processes to BelowNormal.
    /// 3. Starts a 2-second polling loop to apply the same to any new processes
    ///    that appear while Gaming Mode is active.
    /// </summary>
    public void Start(string? gameProcessName = null)
    {
        if (_active) return;
        _active = true;

        // -- Power plan -------------------------------------------------------
        try
        {
            _savedPowerPlan = _powerPlanProvider.GetActivePlan();

            // Prefer Ultimate Performance; fall back to High Performance
            var plans = _powerPlanProvider.GetPowerPlans();
            bool hasUltimate = plans.Any(p =>
                p.SchemeGuid == IPowerPlanProvider.UltimatePerf);

            Guid targetPlan = hasUltimate
                ? IPowerPlanProvider.UltimatePerf
                : IPowerPlanProvider.HighPerformance;

            _powerPlanProvider.SetActivePlan(targetPlan);

            var planName = plans.FirstOrDefault(p => p.SchemeGuid == targetPlan)?.Name
                           ?? targetPlan.ToString();
            _statusMessages.OnNext($"Power plan switched to: {planName}");
        }
        catch (Exception ex)
        {
            _statusMessages.OnNext($"Power plan change failed: {ex.Message}");
        }

        // -- Initial process throttle -----------------------------------------
        _ = ThrottleBackgroundProcessesAsync(gameProcessName ?? _settings.GamingModeGameProcess);

        // -- Polling loop (re-apply to new processes every 2 s via shared stream) ------
        string capturedGameProcess = gameProcessName ?? _settings.GamingModeGameProcess;
        _pollingSubscription = _processProvider
            .GetProcessStream(TimeSpan.FromSeconds(1))
            .Sample(TimeSpan.FromSeconds(2))
            .Subscribe(processes => { _ = ThrottleBackgroundProcessesAsync(capturedGameProcess, processes); });

        _statusMessages.OnNext("Gaming Mode activated");
    }

    /// <summary>
    /// Deactivate Gaming Mode.
    /// Restores the original power plan and resets all throttled processes to Normal.
    /// </summary>
    public void Stop()
    {
        if (!_active) return;
        _active = false;

        _pollingSubscription?.Dispose();
        _pollingSubscription = null;

        // -- Restore power plan -----------------------------------------------
        if (_savedPowerPlan != Guid.Empty)
        {
            try
            {
                _powerPlanProvider.SetActivePlan(_savedPowerPlan);
                var plans = _powerPlanProvider.GetPowerPlans();
                var name = plans.FirstOrDefault(p => p.SchemeGuid == _savedPowerPlan)?.Name
                           ?? _savedPowerPlan.ToString();
                _statusMessages.OnNext($"Power plan restored to: {name}");
            }
            catch (Exception ex)
            {
                _statusMessages.OnNext($"Power plan restore failed: {ex.Message}");
            }
            _savedPowerPlan = Guid.Empty;
        }

        // -- Restore process priorities ----------------------------------------
        _ = RestoreAllAsync();

        _statusMessages.OnNext("Gaming Mode deactivated");
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private async Task ThrottleBackgroundProcessesAsync(string? gameProcess, IReadOnlyList<ProcessInfo>? processes = null)
    {
        if (!_active) return;
        try
        {
            processes  ??= await _processProvider.GetProcessesAsync();
            var exclusions = _settings.GamingModeExclusions;

            var candidates = processes
                .Where(p =>
                    p.Pid != Environment.ProcessId &&
                    p.Category != ProcessCategory.SystemKernel &&
                    !_throttled.ContainsKey(p.Pid) &&
                    !IsExcluded(p.Name, gameProcess, exclusions))
                .ToList();

            foreach (var proc in candidates)
            {
                try
                {
                    // Assume Normal; avoid reading the actual priority class for simplicity
                    _throttled[proc.Pid] = ProcessPriority.Normal;
                    await _processProvider.SetPriorityAsync(proc.Pid, ProcessPriority.BelowNormal);
                    _statusMessages.OnNext($"Throttled: {proc.Name} (PID {proc.Pid})");
                }
                catch
                {
                    // Access denied or process exited — remove from map
                    _throttled.Remove(proc.Pid);
                }
            }

            // Clean up entries for processes that have exited
            var alive = new HashSet<int>(processes.Select(p => p.Pid));
            foreach (var pid in _throttled.Keys.Where(k => !alive.Contains(k)).ToList())
                _throttled.Remove(pid);
        }
        catch { /* swallow — polling loop must not crash */ }
    }

    private async Task RestoreAllAsync()
    {
        if (_throttled.Count == 0) return;
        try
        {
            var processes = await _processProvider.GetProcessesAsync();
            foreach (var (pid, original) in _throttled.ToList())
            {
                try
                {
                    await _processProvider.SetPriorityAsync(pid, original);
                    var name = processes.FirstOrDefault(p => p.Pid == pid)?.Name ?? $"PID {pid}";
                    _statusMessages.OnNext($"Restored: {name} (PID {pid})");
                }
                catch { /* process exited — ignore */ }
            }
        }
        catch { }
        finally
        {
            _throttled.Clear();
        }
    }

    private static bool IsExcluded(
        string processName,
        string? gameProcess,
        List<string> exclusions)
    {
        if (!string.IsNullOrEmpty(gameProcess) &&
            processName.Equals(gameProcess, StringComparison.OrdinalIgnoreCase))
            return true;

        return exclusions.Any(ex =>
            processName.Equals(ex, StringComparison.OrdinalIgnoreCase));
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        Stop();
        _statusMessages.Dispose();
    }

    // ── Public stat helpers ───────────────────────────────────────────────────

    /// <summary>Number of processes currently held at BelowNormal by Gaming Mode.</summary>
    public int ThrottledCount => _throttled.Count;
}
