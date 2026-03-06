using System.Reactive.Linq;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Automation;

/// <summary>
/// Throttles individual background processes that have been idle (low CPU) for
/// a sustained period. Unlike ProBalance (which reacts to system-wide load),
/// IdleSaver targets individual idleness regardless of overall load.
/// </summary>
public sealed class IdleSaverService : IDisposable
{
    private readonly IProcessProvider         _processProvider;
    private readonly IForegroundWindowProvider _foregroundWindow;
    private readonly AppSettings              _settings;
    private readonly ProcessActionLock        _actionLock;

    // pid → consecutive idle tick count
    private readonly Dictionary<int, int>             _idleTicks        = new();
    // pid → priority before we throttled it
    private readonly Dictionary<int, ProcessPriority> _savedPriorities  = new();
    // pid → whether we also enabled efficiency mode
    private readonly HashSet<int>                     _efficiencyPids   = new();

    private IDisposable? _subscription;
    private bool _running;

    private const string Owner = "IdleSaver";

    public bool IsRunning => _running;

    public IdleSaverService(
        IProcessProvider          processProvider,
        IForegroundWindowProvider foregroundWindow,
        AppSettings               settings,
        ProcessActionLock         actionLock)
    {
        _processProvider  = processProvider;
        _foregroundWindow = foregroundWindow;
        _settings         = settings;
        _actionLock       = actionLock;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _subscription = _processProvider
            .GetProcessStream(TimeSpan.FromSeconds(2))
            .Subscribe(OnTick, _ => { });
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _subscription?.Dispose();
        _subscription = null;
        _ = RestoreAllAsync();
    }

    private async void OnTick(IReadOnlyList<ProcessInfo> processes)
    {
        if (!_settings.IdleSaverEnabled) return;

        var fgPid      = _foregroundWindow.GetForegroundProcessId();
        var exclusions = _settings.IdleSaverExclusions;
        var threshold  = _settings.IdleSaverCpuThreshold;
        var ticksReq   = _settings.IdleSaverIdleTicksRequired;
        var useEco     = _settings.IdleSaverUseEfficiencyMode;
        var alive      = new HashSet<int>(processes.Select(p => p.Pid));

        // Evict dead PIDs
        foreach (var pid in _idleTicks.Keys.Where(k => !alive.Contains(k)).ToList())
        {
            _idleTicks.Remove(pid);
            _savedPriorities.Remove(pid);
            _efficiencyPids.Remove(pid);
            _actionLock.Release(pid, Owner);
        }

        foreach (var proc in processes)
        {
            if (proc.Pid == fgPid) continue;
            if (proc.Pid == Environment.ProcessId) continue;
            if (proc.Category == ProcessCategory.SystemKernel) continue;
            if (exclusions.Any(ex => proc.Name.Equals(ex, StringComparison.OrdinalIgnoreCase))) continue;

            bool isThrottled = _savedPriorities.ContainsKey(proc.Pid);

            if (isThrottled)
            {
                // Check if process became active again (high CPU or became foreground)
                bool restored = proc.CpuPercent > threshold * 2.0;
                if (restored)
                    await RestorePidAsync(proc.Pid);
                else
                    _idleTicks[proc.Pid] = 0; // stay throttled, reset counter
            }
            else
            {
                // Track idle ticks
                if (proc.CpuPercent < threshold)
                {
                    _idleTicks.TryGetValue(proc.Pid, out var ticks);
                    ticks++;
                    _idleTicks[proc.Pid] = ticks;

                    if (ticks >= ticksReq && !_actionLock.IsLocked(proc.Pid))
                    {
                        if (_actionLock.TryLock(proc.Pid, Owner))
                        {
                            _savedPriorities[proc.Pid] = ProcessPriority.Normal;
                            try
                            {
                                await _processProvider.SetPriorityAsync(proc.Pid, ProcessPriority.BelowNormal);
                                if (useEco)
                                {
                                    await _processProvider.SetEfficiencyModeAsync(proc.Pid, true);
                                    _efficiencyPids.Add(proc.Pid);
                                }
                            }
                            catch
                            {
                                _savedPriorities.Remove(proc.Pid);
                                _efficiencyPids.Remove(proc.Pid);
                                _actionLock.Release(proc.Pid, Owner);
                            }
                        }
                    }
                }
                else
                {
                    _idleTicks.Remove(proc.Pid);
                }
            }
        }
    }

    private async Task RestorePidAsync(int pid)
    {
        if (_savedPriorities.TryGetValue(pid, out var priority))
        {
            try { await _processProvider.SetPriorityAsync(pid, priority); } catch { }
            _savedPriorities.Remove(pid);
        }
        if (_efficiencyPids.Remove(pid))
        {
            try { await _processProvider.SetEfficiencyModeAsync(pid, false); } catch { }
        }
        _idleTicks.Remove(pid);
        _actionLock.Release(pid, Owner);
    }

    private async Task RestoreAllAsync()
    {
        foreach (var pid in _savedPriorities.Keys.ToList())
            await RestorePidAsync(pid);
    }

    public void Dispose() => Stop();
}
