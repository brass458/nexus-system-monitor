namespace NexusMonitor.DiskAnalyzer.Models;

public sealed class ScanResult
{
    public DiskNode Root { get; init; } = new();
    public string ScannedPath { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public long TotalFiles { get; init; }
    public long TotalFolders { get; init; }
    public long TotalSize { get; init; }
    public DateTime ScanTime { get; init; } = DateTime.UtcNow;
    public string FileSystem { get; init; } = string.Empty;
    public long VolumeTotal { get; init; }
    public long VolumeFree { get; init; }
}

public sealed class ScanProgress
{
    public long FilesScanned { get; init; }
    public long BytesCounted { get; init; }
    public string CurrentPath { get; init; } = string.Empty;
}
