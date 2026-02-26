using System.Diagnostics;
using System.Reactive.Linq;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Platform.MacOS;

public sealed class MacOSProcessProvider : IProcessProvider, IDisposable
{
    private readonly Dictionary<int, (TimeSpan cpu, DateTime time)> _cpuSamples = new();
    private static readonly int s_processorCount = Math.Max(1, Environment.ProcessorCount);
    private static readonly int s_currentPid = Environment.ProcessId;

    private bool _disposed;

    // ── Streaming ──────────────────────────────────────────────────────────────
    public IObservable<IReadOnlyList<ProcessInfo>> GetProcessStream(TimeSpan interval) =>
        Observable.Timer(TimeSpan.Zero, interval)
                  .Select(_ => (IReadOnlyList<ProcessInfo>)Snapshot());

    // ── Snapshot ───────────────────────────────────────────────────────────────
    public Task<IReadOnlyList<ProcessInfo>> GetProcessesAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<ProcessInfo>>(() => Snapshot(), ct);

    private IReadOnlyList<ProcessInfo> Snapshot()
    {
        var now = DateTime.UtcNow;
        var result = new List<ProcessInfo>();

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                using (proc)
                {
                    var pid    = proc.Id;
                    var name   = proc.ProcessName;
                    var ws     = proc.WorkingSet64;
                    var virt   = proc.VirtualMemorySize64;
                    var threads = 0;
                    var handles = 0;
                    var startTime = DateTime.MinValue;
                    var imagePath = string.Empty;
                    var cpu   = TimeSpan.Zero;
                    var accessDenied = false;

                    try { threads   = proc.Threads.Count; }  catch { }
                    try { handles   = proc.HandleCount; }    catch { }
                    try { startTime = proc.StartTime.ToUniversalTime(); } catch { }
                    try { imagePath = proc.MainModule?.FileName ?? string.Empty; } catch { }

                    try
                    {
                        cpu = proc.TotalProcessorTime;
                    }
                    catch
                    {
                        accessDenied = true;
                    }

                    // CPU% delta
                    double cpuPercent = 0.0;
                    if (!accessDenied && _cpuSamples.TryGetValue(pid, out var prev))
                    {
                        var cpuDelta  = (cpu - prev.cpu).TotalSeconds;
                        var timeDelta = (now - prev.time).TotalSeconds;
                        if (timeDelta > 0)
                            cpuPercent = Math.Clamp(cpuDelta / (timeDelta * s_processorCount) * 100.0, 0, 100);
                    }
                    if (!accessDenied)
                        _cpuSamples[pid] = (cpu, now);

                    // Category heuristics
                    var category = ClassifyProcess(pid, imagePath);

                    result.Add(new ProcessInfo
                    {
                        Pid              = pid,
                        ParentPid        = 0,          // requires sysctl KERN_PROC — deferred
                        Name             = name,
                        Description      = string.Empty,
                        ImagePath        = imagePath,
                        CommandLine      = string.Empty,
                        UserName         = string.Empty, // requires sysctl KERN_PROC — deferred
                        Category         = category,
                        State            = ProcessState.Running,
                        StartTime        = startTime,
                        CpuPercent       = cpuPercent,
                        ThreadCount      = threads,
                        HandleCount      = handles,
                        WorkingSetBytes  = ws,
                        PrivateBytesBytes = 0,
                        PagedPoolBytes   = 0,
                        VirtualBytesBytes = virt,
                        IoReadBytesPerSec  = 0,
                        IoWriteBytesPerSec = 0,
                        GpuPercent       = 0,
                        NetworkSendBytesPerSec = 0,
                        NetworkRecvBytesPerSec = 0,
                        IsElevated       = false,
                        IsCritical       = false,
                        AccessDenied     = accessDenied,
                    });
                }
            }
            catch
            {
                // Process exited between GetProcesses() and property access — skip
            }
        }

        // Evict stale CPU samples
        var activePids = new HashSet<int>(result.Select(p => p.Pid));
        foreach (var key in _cpuSamples.Keys.Where(k => !activePids.Contains(k)).ToList())
            _cpuSamples.Remove(key);

        return result;
    }

    private static ProcessCategory ClassifyProcess(int pid, string imagePath)
    {
        if (pid == s_currentPid)
            return ProcessCategory.CurrentProcess;

        if (!string.IsNullOrEmpty(imagePath))
        {
            if (imagePath.StartsWith("/System/", StringComparison.Ordinal)
             || imagePath.StartsWith("/usr/",    StringComparison.Ordinal)
             || imagePath.StartsWith("/sbin/",   StringComparison.Ordinal))
                return ProcessCategory.SystemKernel;

            if (imagePath.StartsWith("/Applications/", StringComparison.Ordinal))
                return ProcessCategory.UserApplication;
        }

        return ProcessCategory.UserApplication;
    }

    // ── Process control ────────────────────────────────────────────────────────
    public Task KillProcessAsync(int pid, bool killTree = false, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            using var p = Process.GetProcessById(pid);
            p.Kill(killTree);
        }, ct);

    public Task SuspendProcessAsync(int pid, CancellationToken ct = default) =>
        Task.FromException(new PlatformNotSupportedException("Process suspend is not supported on macOS via managed APIs."));

    public Task ResumeProcessAsync(int pid, CancellationToken ct = default) =>
        Task.FromException(new PlatformNotSupportedException("Process resume is not supported on macOS via managed APIs."));

    public Task SetAffinityAsync(int pid, long affinityMask, CancellationToken ct = default) =>
        Task.FromException(new PlatformNotSupportedException("CPU affinity is not supported on macOS."));

    public Task SetPriorityAsync(int pid, ProcessPriority priority, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            using var p = Process.GetProcessById(pid);
            p.PriorityClass = priority switch
            {
                ProcessPriority.Idle        => ProcessPriorityClass.Idle,
                ProcessPriority.BelowNormal => ProcessPriorityClass.BelowNormal,
                ProcessPriority.Normal      => ProcessPriorityClass.Normal,
                ProcessPriority.AboveNormal => ProcessPriorityClass.AboveNormal,
                ProcessPriority.High        => ProcessPriorityClass.High,
                ProcessPriority.RealTime    => ProcessPriorityClass.RealTime,
                _                           => ProcessPriorityClass.Normal,
            };
        }, ct);

    // ── Detail queries (deferred) ──────────────────────────────────────────────
    public Task<IReadOnlyList<ModuleInfo>> GetModulesAsync(int pid, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ModuleInfo>>([]);

    public Task<IReadOnlyList<ThreadInfo>> GetThreadsAsync(int pid, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ThreadInfo>>([]);

    public Task<IReadOnlyList<EnvironmentEntry>> GetEnvironmentAsync(int pid, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EnvironmentEntry>>([]); // sysctl KERN_PROCARGS2 required

    // ── IDisposable ────────────────────────────────────────────────────────────
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cpuSamples.Clear();
    }
}
