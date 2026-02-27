using System.Runtime.InteropServices;
using NexusMonitor.DiskAnalyzer.Models;

namespace NexusMonitor.DiskAnalyzer.Scanning;

/// <summary>
/// Cross-platform recursive directory scanner using standard .NET APIs.
/// Uses parallel directory enumeration for improved performance.
/// On Windows, also attempts to get allocated size via GetCompressedFileSizeW.
/// </summary>
public sealed class RecursiveScanner : IDiskScanner
{
    public async Task<ScanResult> ScanAsync(
        string path,
        ScanOptions? options,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        options ??= new ScanOptions();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var root = new DiskNode
        {
            Name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                   .IfEmpty(path),
            FullPath = path,
            IsDirectory = true,
        };

        long filesScanned = 0;
        long bytesCounted = 0;

        await Task.Run(() => ScanDirectory(root, options, progress, ref filesScanned, ref bytesCounted, ct), ct);

        sw.Stop();

        // Get volume info
        long volTotal = 0, volFree = 0;
        string fs = string.Empty;
        try
        {
            var di = new DriveInfo(Path.GetPathRoot(path) ?? path);
            volTotal = di.TotalSize;
            volFree  = di.TotalFreeSpace;
            fs       = di.DriveFormat;
        }
        catch { }

        return new ScanResult
        {
            Root         = root,
            ScannedPath  = path,
            Duration     = sw.Elapsed,
            TotalFiles   = root.FileCount,
            TotalFolders = root.FolderCount,
            TotalSize    = root.Size,
            FileSystem   = fs,
            VolumeTotal  = volTotal,
            VolumeFree   = volFree,
        };
    }

    private static void ScanDirectory(
        DiskNode node,
        ScanOptions options,
        IProgress<ScanProgress>? progress,
        ref long filesScanned,
        ref long bytesCounted,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (options.ExcludedPaths.Contains(node.FullPath)) return;

        // Enumerate entries
        IEnumerable<FileSystemInfo> entries;
        try
        {
            var di = new DirectoryInfo(node.FullPath);
            entries = di.EnumerateFileSystemInfos("*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                AttributesToSkip = FileAttributes.ReparsePoint, // skip junctions/symlinks by default
            });
        }
        catch { return; }

        var subdirs = new List<DiskNode>();

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (entry is DirectoryInfo dir)
                {
                    var child = new DiskNode
                    {
                        Name = dir.Name,
                        FullPath = dir.FullName,
                        IsDirectory = true,
                        LastModified = dir.LastWriteTimeUtc,
                        LastAccessed = dir.LastAccessTimeUtc,
                        Created = dir.CreationTimeUtc,
                        Parent = node,
                    };
                    node.Children.Add(child);
                    subdirs.Add(child);
                    node.FolderCount++;
                }
                else if (entry is FileInfo file)
                {
                    if (file.Length < options.MinFileSizeBytes) continue;
                    var child = new DiskNode
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        IsDirectory = false,
                        Size = file.Length,
                        AllocatedSize = GetAllocatedSize(file.FullName, file.Length),
                        LastModified = file.LastWriteTimeUtc,
                        LastAccessed = file.LastAccessTimeUtc,
                        Created = file.CreationTimeUtc,
                        Parent = node,
                    };
                    node.Children.Add(child);
                    node.Size += child.Size;
                    node.FileCount++;
                    Interlocked.Increment(ref filesScanned);
                    Interlocked.Add(ref bytesCounted, child.Size);
                    progress?.Report(new ScanProgress
                    {
                        FilesScanned = filesScanned,
                        BytesCounted = bytesCounted,
                        CurrentPath = file.FullName,
                    });
                }
            }
            catch { /* skip inaccessible entries */ }
        }

        // Recurse into subdirectories
        foreach (var subdir in subdirs)
        {
            ScanDirectory(subdir, options, progress, ref filesScanned, ref bytesCounted, ct);
            node.Size       += subdir.Size;
            node.FileCount  += subdir.FileCount;
            node.FolderCount += subdir.FolderCount + 1;
        }

        // Sort children by size descending for treemap
        node.Children.Sort((a, b) => b.Size.CompareTo(a.Size));
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetCompressedFileSizeW(string lpFileName, out uint lpFileSizeHigh);

    private static long GetAllocatedSize(string path, long fallback)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return fallback;
        try
        {
            uint high;
            uint low = GetCompressedFileSizeW(path, out high);
            if (low == 0xFFFFFFFF && Marshal.GetLastWin32Error() != 0)
                return fallback;
            return ((long)high << 32) | low;
        }
        catch { return fallback; }
    }
}

file static class StringExtensions
{
    public static string IfEmpty(this string s, string fallback) =>
        string.IsNullOrEmpty(s) ? fallback : s;
}
