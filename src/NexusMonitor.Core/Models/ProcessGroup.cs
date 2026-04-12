using NexusMonitor.Core.Matching;

namespace NexusMonitor.Core.Models;

/// <summary>
/// Represents a named collection of wildcard patterns for grouping processes.
/// Groups are used to organize related processes and apply rules or actions to matching processes.
/// </summary>
public class ProcessGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Group";
    public string Color { get; set; } = "#5B9BD5";  // hex color for UI badge
    public List<string> Patterns { get; set; } = [];
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Returns true if processName matches any pattern in this group.
    /// Patterns are normalized per-call (unlike ProcessRule's pre-normalized _normalizedPattern)
    /// because Patterns is a mutable List&lt;string&gt; — caching normalized forms would silently
    /// break if a caller appends or replaces patterns after construction.
    /// </summary>
    public bool Matches(string processName) =>
        Patterns.Any(p => WildcardMatcher.Matches(
            WildcardMatcher.NormalizeName(processName),
            WildcardMatcher.NormalizePattern(p)));
}
