using NexusMonitor.DiskAnalyzer.Models;

namespace NexusMonitor.DiskAnalyzer.Scanning;

public sealed class ScanOptions
{
    public bool FollowSymlinks { get; set; } = false;
    public HashSet<string> ExcludedPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public long MinFileSizeBytes { get; set; } = 0;
}

public interface IDiskScanner
{
    /// <summary>Scans a path and reports progress. Returns the completed tree.</summary>
    Task<ScanResult> ScanAsync(
        string path,
        ScanOptions? options,
        IProgress<ScanProgress>? progress,
        CancellationToken ct);
}
