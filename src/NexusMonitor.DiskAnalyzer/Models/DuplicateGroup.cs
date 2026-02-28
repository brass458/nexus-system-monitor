namespace NexusMonitor.DiskAnalyzer.Models;

public sealed class DuplicateGroup
{
    public string Hash { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public List<DiskNode> Files { get; } = new();
    public long WastedBytes => FileSize * (Files.Count - 1);
    public string WastedDisplay => DiskNode.FormatSize(WastedBytes);
}
