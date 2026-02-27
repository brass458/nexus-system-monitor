using NexusMonitor.DiskAnalyzer.Models;
using System.Security.Cryptography;

namespace NexusMonitor.DiskAnalyzer.Analysis;

public sealed class DuplicateFinder
{
    /// <summary>
    /// Two-pass duplicate detection:
    /// 1. Group by size (fast — no I/O beyond what we already have)
    /// 2. Hash only files with matching sizes (minimizes disk reads)
    /// </summary>
    public async Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync(
        DiskNode root,
        IProgress<(int done, int total)>? progress,
        CancellationToken ct)
    {
        // Collect all files
        var allFiles = new List<DiskNode>();
        CollectFiles(root, allFiles);

        // Pass 1: group by size
        var bySize = allFiles
            .Where(f => f.Size > 0)
            .GroupBy(f => f.Size)
            .Where(g => g.Count() > 1)
            .ToList();

        if (bySize.Count == 0) return [];

        int total = bySize.Sum(g => g.Count());
        int done = 0;
        var groups = new Dictionary<string, DuplicateGroup>();

        // Pass 2: hash files within each size group
        foreach (var sizeGroup in bySize)
        {
            long groupSize = sizeGroup.Key;
            foreach (var file in sizeGroup)
            {
                ct.ThrowIfCancellationRequested();
                string hash;
                try   { hash = await HashFileAsync(file.FullPath, ct); }
                catch { done++; continue; }

                if (!groups.TryGetValue(hash, out var group))
                {
                    group = new DuplicateGroup { Hash = hash, FileSize = groupSize };
                    groups[hash] = group;
                }
                group.Files.Add(file);
                done++;
                progress?.Report((done, total));
            }
        }

        return groups.Values.Where(g => g.Files.Count > 1).OrderByDescending(g => g.WastedBytes).ToList();
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        // Read first 64KB for fast pre-screening
        const int previewBytes = 65536;
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 65536, useAsync: true);
        using var sha = SHA256.Create();

        int readLen = (int)Math.Min(previewBytes, fs.Length);
        var buffer = new byte[readLen];
        await fs.ReadExactlyAsync(buffer, ct);
        return Convert.ToHexString(sha.ComputeHash(buffer));
    }

    private static void CollectFiles(DiskNode node, List<DiskNode> output)
    {
        if (!node.IsDirectory) { output.Add(node); return; }
        foreach (var child in node.Children) CollectFiles(child, output);
    }
}
