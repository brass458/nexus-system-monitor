namespace NexusMonitor.Core.Matching;

/// <summary>
/// Shared utility for process name wildcard matching.
/// Used by <see cref="NexusMonitor.Core.Rules.ProcessRule"/> and ProcessGroup.
/// </summary>
public static class WildcardMatcher
{
    /// <summary>
    /// Normalizes a process name pattern: strips .exe suffix (case-insensitive) and lowercases.
    /// </summary>
    public static string NormalizePattern(string pattern) =>
        (pattern ?? "")
            .Replace(".exe", "", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();

    /// <summary>
    /// Normalizes a process name: strips .exe suffix via <see cref="Path.GetFileNameWithoutExtension"/>
    /// and lowercases.
    /// </summary>
    public static string NormalizeName(string input) =>
        Path.GetFileNameWithoutExtension(input).ToLowerInvariant();

    /// <summary>
    /// Returns true if <paramref name="normalizedName"/> matches <paramref name="normalizedPattern"/>.
    /// Both inputs must already be normalized (use <see cref="NormalizeName"/> /
    /// <see cref="NormalizePattern"/> first).
    /// Supports <c>*</c> as a wildcard; no <c>*</c> means exact match.
    /// </summary>
    public static bool Matches(string normalizedName, string normalizedPattern)
    {
        if (!normalizedPattern.Contains('*')) return normalizedName == normalizedPattern;
        // Split on * and verify each part appears in order
        var parts = normalizedPattern.Split('*');
        int idx = 0;
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;
            var found = normalizedName.IndexOf(part, idx, StringComparison.Ordinal);
            if (found < 0) return false;
            idx = found + part.Length;
        }
        return true;
    }
}
