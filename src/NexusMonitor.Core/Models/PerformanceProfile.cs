using NexusMonitor.Core.Abstractions;

namespace NexusMonitor.Core.Models;

/// <summary>
/// A named collection of process rules and optional power plan change
/// that can be activated on demand to boost or throttle specific workloads.
/// </summary>
public class PerformanceProfile
{
    public Guid   Id          { get; set; } = Guid.NewGuid();
    public string Name        { get; set; } = "New Profile";
    public string Description { get; set; } = "";

    public List<ProfileProcessRule> ProcessRules { get; set; } = new();

    public bool   ChangePowerPlan { get; set; }
    /// <summary>"High Performance", "Ultimate Performance", "Balanced", etc.</summary>
    public string PowerPlanName   { get; set; } = "";
}

/// <summary>
/// A single process rule within a <see cref="PerformanceProfile"/>.
/// Supports wildcard matching (same rules as ProcessRule).
/// </summary>
public class ProfileProcessRule
{
    /// <summary>Process name to match — case-insensitive, * wildcard, no .exe needed.</summary>
    public string ProcessNamePattern { get; set; } = "";
    public ProcessPriority? Priority    { get; set; }
    public bool?            EfficiencyMode { get; set; }

    /// <summary>String label for UI binding — "" or "(none)" means no priority change.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string PriorityLabel
    {
        get => Priority.HasValue ? Priority.Value.ToString() : "";
        set
        {
            if (string.IsNullOrEmpty(value) || value == "(none)")
                Priority = null;
            else if (Enum.TryParse<ProcessPriority>(value, out var p))
                Priority = p;
        }
    }

    public bool Matches(string processName)
    {
        if (string.IsNullOrWhiteSpace(ProcessNamePattern)) return false;
        var name    = System.IO.Path.GetFileNameWithoutExtension(processName).ToLowerInvariant();
        var pattern = ProcessNamePattern
            .Replace(".exe", "", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();
        if (!pattern.Contains('*')) return name == pattern;
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
