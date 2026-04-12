using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Matching;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Rules;

public enum WatchdogAction
{
    None,
    SetBelowNormal,
    SetIdle,
    Terminate,
    ReduceAffinity,
    SetIoPriorityLow,
    SetEfficiencyMode,
    TrimWorkingSet,
    Restart,
    LogOnly
}

public enum ConditionType  { Always, CpuAbove, RamAbove }

public class WatchdogActionParams
{
    public int? ReduceCoreCount { get; set; }
}

public class RuleCondition
{
    public ConditionType Type                { get; set; } = ConditionType.Always;
    public double        CpuThresholdPercent { get; set; } = 25.0;
    public long          RamThresholdBytes   { get; set; } = 536_870_912L; // 512 MB
    /// <summary>How long condition must be sustained before action fires.</summary>
    public int           DurationSeconds     { get; set; } = 5;
}

public class ProcessRule
{
    public Guid   Id                  { get; set; } = Guid.NewGuid();
    public string Name                { get; set; } = "New Rule";
    /// <summary>Process name to match (case-insensitive, * wildcard supported, no .exe needed).</summary>
    private string _processNamePattern = "";
    private string _normalizedPattern  = "";

    public string ProcessNamePattern
    {
        get => _processNamePattern;
        set
        {
            _processNamePattern = value;
            // Pre-normalize once so Matches never allocates per call
            _normalizedPattern = WildcardMatcher.NormalizePattern(value ?? "");
        }
    }
    public bool   IsEnabled           { get; set; } = true;

    // ── Persistent actions (applied on every process launch) ──────────────
    public ProcessPriority? Priority       { get; set; }
    public long?            AffinityMask   { get; set; }
    public IoPriority?      IoPriority     { get; set; }
    public MemoryPriority?  MemoryPriority { get; set; }
    public bool?            EfficiencyMode { get; set; }

    // ── Watchdog / conditional actions ───────────────────────────────────
    public RuleCondition?      Condition       { get; set; }
    public WatchdogAction      WatchdogAction  { get; set; } = WatchdogAction.None;
    public WatchdogActionParams? ActionParams  { get; set; }
    public bool                KeepRunning     { get; set; } = false; // auto-restart on exit
    public int                 KeepRunningMaxRetries       { get; set; } = 3;
    public int                 KeepRunningCooldownSeconds  { get; set; } = 5;
    public bool                Disallowed      { get; set; } = false; // auto-terminate on launch
    public int?                MaxInstances    { get; set; }
    public bool                PreventSleep    { get; set; } = false;
    public uint[]?             CpuSetIds       { get; set; }

    // ── Display ───────────────────────────────────────────────────────────
    public string Summary => BuildSummary();

    private string BuildSummary()
    {
        var parts = new List<string>();
        if (Priority.HasValue)      parts.Add($"Priority={Priority}");
        if (AffinityMask.HasValue)  parts.Add($"Affinity=0x{AffinityMask:X}");
        if (IoPriority.HasValue)    parts.Add($"IO={IoPriority}");
        if (EfficiencyMode == true) parts.Add("Efficiency");
        if (Disallowed)             parts.Add("Block");
        if (KeepRunning)            parts.Add("KeepRunning");
        if (MaxInstances.HasValue)  parts.Add($"MaxInstances={MaxInstances}");
        if (PreventSleep)           parts.Add("PreventSleep");
        if (CpuSetIds?.Length > 0)  parts.Add($"CpuSets=[{string.Join(",", CpuSetIds!)}]");
        if (WatchdogAction != WatchdogAction.None) parts.Add($"Watchdog={WatchdogAction}");
        return parts.Count == 0 ? "(no actions)" : string.Join(", ", parts);
    }

    public bool Matches(string processName) =>
        !string.IsNullOrWhiteSpace(_normalizedPattern) &&
        WildcardMatcher.Matches(WildcardMatcher.NormalizeName(processName), _normalizedPattern);
}
