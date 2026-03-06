using NexusMonitor.Core.Abstractions;

namespace NexusMonitor.Core.Models;

/// <summary>
/// Persistent per-exe settings that survive across process restarts.
/// Applied automatically by the RulesEngine each time the exe launches.
/// </summary>
public class ProcessPreference
{
    /// <summary>Normalized exe name: lowercase, no .exe extension.</summary>
    public string ExeName { get; set; } = "";
    public ProcessPriority? Priority { get; set; }
    public long? AffinityMask { get; set; }
    public IoPriority? IoPriority { get; set; }
    public MemoryPriority? MemoryPriority { get; set; }
    public bool? EfficiencyMode { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    public static string NormalizeExeName(string name) =>
        System.IO.Path.GetFileNameWithoutExtension(name)
              .Replace(".exe", "", StringComparison.OrdinalIgnoreCase)
              .ToLowerInvariant();

    /// <summary>Human-readable summary of applied settings.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Priority.HasValue)      parts.Add($"Priority={Priority}");
            if (AffinityMask.HasValue)  parts.Add($"Affinity=0x{AffinityMask:X}");
            if (IoPriority.HasValue)    parts.Add($"IO={IoPriority}");
            if (MemoryPriority.HasValue)parts.Add($"Memory={MemoryPriority}");
            if (EfficiencyMode == true) parts.Add("Efficiency");
            return parts.Count == 0 ? "(no settings)" : string.Join(", ", parts);
        }
    }
}
