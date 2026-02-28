using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Rules;

public enum WatchdogAction { None, SetBelowNormal, SetIdle, Terminate }
public enum ConditionType  { Always, CpuAbove, RamAbove }

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
    public string ProcessNamePattern  { get; set; } = "";
    public bool   IsEnabled           { get; set; } = true;

    // ── Persistent actions (applied on every process launch) ──────────────
    public ProcessPriority? Priority       { get; set; }
    public long?            AffinityMask   { get; set; }
    public IoPriority?      IoPriority     { get; set; }
    public MemoryPriority?  MemoryPriority { get; set; }
    public bool?            EfficiencyMode { get; set; }

    // ── Watchdog / conditional actions ───────────────────────────────────
    public RuleCondition? Condition      { get; set; }
    public WatchdogAction WatchdogAction { get; set; } = WatchdogAction.None;
    public bool           KeepRunning    { get; set; } = false; // auto-restart on exit
    public bool           Disallowed     { get; set; } = false; // auto-terminate on launch

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
        if (WatchdogAction != WatchdogAction.None) parts.Add($"Watchdog={WatchdogAction}");
        return parts.Count == 0 ? "(no actions)" : string.Join(", ", parts);
    }

    public bool Matches(string processName) =>
        !string.IsNullOrWhiteSpace(ProcessNamePattern) &&
        MatchesWildcard(processName, ProcessNamePattern);

    private static bool MatchesWildcard(string input, string pattern)
    {
        // Strip .exe if present for comparison
        var name = System.IO.Path.GetFileNameWithoutExtension(input).ToLowerInvariant();
        pattern = pattern.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        if (!pattern.Contains('*')) return name == pattern;
        // Simple wildcard: split on * and check that each part appears in order
        var parts = pattern.Split('*');
        int idx = 0;
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;
            var found = name.IndexOf(part, idx, StringComparison.Ordinal);
            if (found < 0) return false;
            idx = found + part.Length;
        }
        return true;
    }
}
