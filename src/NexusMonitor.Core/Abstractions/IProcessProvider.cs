using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Abstractions;

public interface IProcessProvider
{
    IObservable<IReadOnlyList<ProcessInfo>> GetProcessStream(TimeSpan interval);
    Task<IReadOnlyList<ProcessInfo>> GetProcessesAsync(CancellationToken ct = default);

    Task KillProcessAsync(int pid, bool killTree = false, CancellationToken ct = default);
    Task SuspendProcessAsync(int pid, CancellationToken ct = default);
    Task ResumeProcessAsync(int pid, CancellationToken ct = default);
    Task SetPriorityAsync(int pid, ProcessPriority priority, CancellationToken ct = default);
    Task SetAffinityAsync(int pid, long affinityMask, CancellationToken ct = default);
    Task<IReadOnlyList<ModuleInfo>> GetModulesAsync(int pid, CancellationToken ct = default);
    Task<IReadOnlyList<ThreadInfo>> GetThreadsAsync(int pid, CancellationToken ct = default);
    Task<IReadOnlyList<EnvironmentEntry>> GetEnvironmentAsync(int pid, CancellationToken ct = default);
}

public enum ProcessPriority { Idle, BelowNormal, Normal, AboveNormal, High, RealTime }
