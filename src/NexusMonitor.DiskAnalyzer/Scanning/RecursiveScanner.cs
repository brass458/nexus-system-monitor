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
        DiskNode root,
        ScanOptions options,
        IProgress<ScanProgress>? progress,
        ref long filesScanned,
        ref long bytesCounted,
        CancellationToken ct)
    {
        // Iterative DFS — avoids stack overflow on deep directory trees (e.g. node_modules).
        // visitOrder contains only directories in DFS pre-order; reversed = post-order for
        // bottom-up size accumulation.
        const int MaxDepth = 256;

        var stack      = new Stack<(DiskNode Node, int Depth)>(64);
        var visitOrder = new List<DiskNode>(128);
        stack.Push((root, 0));

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (node, depth) = stack.Pop();

            if (options.ExcludedPaths.Contains(node.FullPath)) continue;
            visitOrder.Add(node);

            IEnumerable<FileSystemInfo> entries;
            try
            {
                var di = new DirectoryInfo(node.FullPath);
                entries = di.EnumerateFileSystemInfos("*", new EnumerationOptions
                {
                    IgnoreInaccessible    = true,
                    RecurseSubdirectories = false,
                    AttributesToSkip      = FileAttributes.ReparsePoint,
                }).ToList(); // materialise so enumeration errors surface here, not lazily
            }
            catch { continue; }

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (entry is DirectoryInfo dir)
                    {
                        var child = new DiskNode
                        {
                            Name         = dir.Name,
                            FullPath     = dir.FullName,
                            IsDirectory  = true,
                            LastModified = dir.LastWriteTimeUtc,
                            LastAccessed = dir.LastAccessTimeUtc,
                            Created      = dir.CreationTimeUtc,
                            Parent       = node,
                        };
                        node.Children.Add(child);
                        if (depth < MaxDepth)
                            stack.Push((child, depth + 1));
                    }
                    else if (entry is FileInfo file)
                    {
                        if (file.Length < options.MinFileSizeBytes) continue;
                        long allocSz = GetAllocatedSize(file.FullName, file.Length);
                        var child = new DiskNode
                        {
                            Name          = file.Name,
                            FullPath      = file.FullName,
                            IsDirectory   = false,
                            Size          = file.Length,
                            AllocatedSize = allocSz,
                            LastModified  = file.LastWriteTimeUtc,
                            LastAccessed  = file.LastAccessTimeUtc,
                            Created       = file.CreationTimeUtc,
                            Parent        = node,
                        };
                        node.Children.Add(child);
                        // Accumulate file contributions into parent immediately
                        node.Size          += file.Length;
                        node.AllocatedSize += allocSz;
                        node.FileCount++;
                        Interlocked.Increment(ref filesScanned);
                        Interlocked.Add(ref bytesCounted, file.Length);
                        progress?.Report(new ScanProgress
                        {
                            FilesScanned = filesScanned,
                            BytesCounted = bytesCounted,
                            CurrentPath  = file.FullName,
                        });
                    }
                }
                catch { /* skip inaccessible entries */ }
            }
        }

        // Post-order: propagate directory totals to parents, then sort children by size.
        // Processing in reverse guarantees every child directory is handled before its parent.
        for (int i = visitOrder.Count - 1; i >= 0; i--)
        {
            var node = visitOrder[i];
            node.Children.Sort((a, b) => b.Size.CompareTo(a.Size));
            if (node.Parent is null) continue;
            var p = node.Parent;
            p.Size          += node.Size;
            p.AllocatedSize += node.AllocatedSize;
            p.FileCount     += node.FileCount;
            p.FolderCount   += node.FolderCount + 1;
        }
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
