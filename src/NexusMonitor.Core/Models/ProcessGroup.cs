using NexusMonitor.Core.Matching;

namespace NexusMonitor.Core.Models;

public class ProcessGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Group";
    public string Color { get; set; } = "#5B9BD5";  // hex color for UI badge
    public List<string> Patterns { get; set; } = [];
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    // Returns true if processName matches any pattern in this group
    public bool Matches(string processName) =>
        Patterns.Any(p => WildcardMatcher.Matches(
            WildcardMatcher.NormalizeName(processName),
            WildcardMatcher.NormalizePattern(p)));
}
