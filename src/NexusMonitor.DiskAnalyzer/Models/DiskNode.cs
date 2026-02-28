namespace NexusMonitor.DiskAnalyzer.Models;

/// <summary>Represents a file or folder in the scanned disk tree.</summary>
public sealed class DiskNode
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Size { get; set; }          // bytes, recursive total for directories
    public long AllocatedSize { get; set; } // actual disk usage (clusters)
    public long FileCount { get; set; }     // recursive file count for directories
    public long FolderCount { get; set; }
    public DateTime LastModified { get; set; }
    public DateTime LastAccessed { get; set; }
    public DateTime Created { get; set; }
    public string Extension => IsDirectory ? string.Empty
        : System.IO.Path.GetExtension(Name).ToLowerInvariant();

    public DiskNode? Parent { get; set; }
    public List<DiskNode> Children { get; } = new();

    // Computed display helpers
    public double PercentOfParent  => Parent?.Size > 0 ? (double)Size / Parent.Size * 100.0 : 100.0;
    public string SizeDisplay      => FormatSize(Size);
    public string AllocatedDisplay => FormatSize(AllocatedSize);
    public string FileCountDisplay   => IsDirectory ? $"{FileCount:N0} files" : string.Empty;
    public string FolderCountDisplay => IsDirectory ? $"{FolderCount:N0} dirs" : string.Empty;

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1_099_511_627_776L => $"{bytes / 1_099_511_627_776.0:F1} TB",
        >= 1_073_741_824L     => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576L         => $"{bytes / 1_048_576.0:F1} MB",
        >= 1_024L             => $"{bytes / 1_024.0:F1} KB",
        _                     => $"{bytes} B",
    };
}
