using SkiaSharp;
using NexusMonitor.DiskAnalyzer.Models;

namespace NexusMonitor.DiskAnalyzer.Analysis;

public record TreemapRect(DiskNode Node, SKRect Bounds, int Depth);

/// <summary>Squarified treemap layout algorithm.</summary>
public static class TreemapLayout
{
    private const float MinRectSize = 3f; // skip rects smaller than 3px — not visible anyway

    public static List<TreemapRect> Layout(DiskNode root, SKRect bounds)
    {
        var output = new List<TreemapRect>(512);
        if (root.Size == 0) return output;
        LayoutChildren(root.Children, bounds, root.Size, output, depth: 0);
        return output;
    }

    private static void LayoutChildren(
        List<DiskNode> children,
        SKRect bounds,
        long totalSize,
        List<TreemapRect> output,
        int depth)
    {
        if (children.Count == 0 || totalSize == 0) return;
        if (bounds.Width < MinRectSize || bounds.Height < MinRectSize) return;

        // Filter out zero-size nodes
        var nodes = children.Where(n => n.Size > 0).ToList();
        if (nodes.Count == 0) return;

        Squarify(nodes, bounds, totalSize, output, depth);
    }

    private static void Squarify(
        List<DiskNode> nodes,
        SKRect bounds,
        long totalSize,
        List<TreemapRect> output,
        int depth)
    {
        if (nodes.Count == 0) return;

        var remaining = new List<DiskNode>(nodes);
        var currentBounds = bounds;
        long remainingSize = totalSize;

        while (remaining.Count > 0 && currentBounds.Width >= MinRectSize && currentBounds.Height >= MinRectSize)
        {
            bool horizontal = currentBounds.Width >= currentBounds.Height;
            float strip = horizontal ? currentBounds.Height : currentBounds.Width;

            var row = BuildRow(remaining, strip, remainingSize, currentBounds);
            PlaceRow(row, currentBounds, remainingSize, horizontal, output, depth);

            long rowSize = row.Sum(n => n.Size);
            remainingSize -= rowSize;
            foreach (var n in row) remaining.Remove(n);

            // Advance bounds past the placed row
            if (remainingSize <= 0) break;

            if (horizontal)
            {
                float usedW = currentBounds.Width * ((float)rowSize / (rowSize + remainingSize));
                currentBounds = new SKRect(
                    currentBounds.Left + usedW, currentBounds.Top,
                    currentBounds.Right, currentBounds.Bottom);
            }
            else
            {
                float usedH = currentBounds.Height * ((float)rowSize / (rowSize + remainingSize));
                currentBounds = new SKRect(
                    currentBounds.Left, currentBounds.Top + usedH,
                    currentBounds.Right, currentBounds.Bottom);
            }
        }
    }

    private static List<DiskNode> BuildRow(List<DiskNode> nodes, float strip, long totalSize, SKRect bounds)
    {
        var row = new List<DiskNode>();
        double bestRatio = double.MaxValue;

        foreach (var node in nodes)
        {
            row.Add(node);
            double ratio = WorstRatio(row, strip, totalSize, bounds);
            if (row.Count > 1 && ratio > bestRatio)
            {
                row.RemoveAt(row.Count - 1);
                break;
            }
            bestRatio = ratio;
        }
        return row;
    }

    private static double WorstRatio(List<DiskNode> row, float strip, long totalSize, SKRect bounds)
    {
        long rowSize = row.Sum(n => n.Size);
        if (rowSize == 0) return double.MaxValue;
        float totalArea = bounds.Width * bounds.Height;
        float rowArea = totalArea * ((float)rowSize / totalSize);
        float stripLen = rowArea / strip;
        double worst = 0;
        foreach (var n in row)
        {
            float cellArea = totalArea * ((float)n.Size / totalSize);
            float h = cellArea / stripLen;
            double ratio = Math.Max((double)stripLen / h, (double)h / stripLen);
            if (ratio > worst) worst = ratio;
        }
        return worst;
    }

    private static void PlaceRow(
        List<DiskNode> row,
        SKRect bounds,
        long totalSize,
        bool horizontal,
        List<TreemapRect> output,
        int depth)
    {
        long rowSize = row.Sum(n => n.Size);
        if (rowSize == 0) return;

        float x = bounds.Left;
        float y = bounds.Top;

        if (horizontal)
        {
            float rowWidth = bounds.Width * ((float)rowSize / totalSize);
            float cellY = y;
            foreach (var node in row)
            {
                float cellH = bounds.Height * ((float)node.Size / rowSize);
                var rect = new SKRect(x, cellY, x + rowWidth, cellY + cellH);
                if (rect.Width >= MinRectSize && rect.Height >= MinRectSize)
                {
                    output.Add(new TreemapRect(node, rect, depth));
                    if (node.IsDirectory && node.Children.Count > 0)
                    {
                        var innerRect = new SKRect(rect.Left + 1, rect.Top + 16, rect.Right - 1, rect.Bottom - 1);
                        if (innerRect.Width > MinRectSize && innerRect.Height > MinRectSize)
                            LayoutChildren(node.Children, innerRect, node.Size, output, depth + 1);
                    }
                }
                cellY += cellH;
            }
        }
        else
        {
            float rowHeight = bounds.Height * ((float)rowSize / totalSize);
            float cellX = x;
            foreach (var node in row)
            {
                float cellW = bounds.Width * ((float)node.Size / rowSize);
                var rect = new SKRect(cellX, y, cellX + cellW, y + rowHeight);
                if (rect.Width >= MinRectSize && rect.Height >= MinRectSize)
                {
                    output.Add(new TreemapRect(node, rect, depth));
                    if (node.IsDirectory && node.Children.Count > 0)
                    {
                        var innerRect = new SKRect(rect.Left + 1, rect.Top + 16, rect.Right - 1, rect.Bottom - 1);
                        if (innerRect.Width > MinRectSize && innerRect.Height > MinRectSize)
                            LayoutChildren(node.Children, innerRect, node.Size, output, depth + 1);
                    }
                }
                cellX += cellW;
            }
        }
    }
}
