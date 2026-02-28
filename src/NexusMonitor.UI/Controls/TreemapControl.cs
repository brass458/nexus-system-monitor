using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using NexusMonitor.DiskAnalyzer.Analysis;
using NexusMonitor.DiskAnalyzer.Models;
using SkiaSharp;

namespace NexusMonitor.UI.Controls;

public class TreemapControl : Control
{
    // ── Dependency Properties ─────────────────────────────────────────────────

    public static readonly StyledProperty<DiskNode?> RootNodeProperty =
        AvaloniaProperty.Register<TreemapControl, DiskNode?>(nameof(RootNode));

    public static readonly StyledProperty<DiskNode?> SelectedNodeProperty =
        AvaloniaProperty.Register<TreemapControl, DiskNode?>(nameof(SelectedNode));

    public static readonly DirectProperty<TreemapControl, DiskNode?> ClickedNodeProperty =
        AvaloniaProperty.RegisterDirect<TreemapControl, DiskNode?>(
            nameof(ClickedNode), o => o.ClickedNode);

    public DiskNode? RootNode
    {
        get => GetValue(RootNodeProperty);
        set => SetValue(RootNodeProperty, value);
    }

    public DiskNode? SelectedNode
    {
        get => GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }

    private DiskNode? _clickedNode;
    public DiskNode? ClickedNode
    {
        get => _clickedNode;
        private set => SetAndRaise(ClickedNodeProperty, ref _clickedNode, value);
    }

    // ── Events ────────────────────────────────────────────────────────────────

    public event Action<DiskNode>? NodeClicked;

    // ── Cached layout ─────────────────────────────────────────────────────────

    private List<TreemapRect>? _cachedLayout;
    private DiskNode?          _cachedRoot;
    private Size               _cachedSize;

    static TreemapControl()
    {
        RootNodeProperty.Changed.AddClassHandler<TreemapControl>((c, _) => c.InvalidateLayout());
        SelectedNodeProperty.Changed.AddClassHandler<TreemapControl>((c, _) => c.InvalidateLayout());
        AffectsRender<TreemapControl>(RootNodeProperty, SelectedNodeProperty);
    }

    private void InvalidateLayout()
    {
        _cachedLayout = null;
        _cachedRoot   = null;
        InvalidateVisual();
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        var displayRoot = SelectedNode ?? RootNode;
        if (displayRoot is null || bounds.Width <= 0 || bounds.Height <= 0) return;

        // Recompute layout if size or data changed
        if (_cachedLayout is null || _cachedRoot != displayRoot || _cachedSize != Bounds.Size)
        {
            _cachedRoot   = displayRoot;
            _cachedSize   = Bounds.Size;
            _cachedLayout = TreemapLayout.Layout(
                displayRoot,
                new SKRect(0, 0, (float)Bounds.Width, (float)Bounds.Height));
        }

        context.Custom(new TreemapDrawOp(bounds, _cachedLayout));
    }

    // ── Hit testing ───────────────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pos = e.GetPosition(this);
        var hit = HitTest((float)pos.X, (float)pos.Y);
        if (hit is not null)
        {
            ClickedNode = hit;
            NodeClicked?.Invoke(hit);
        }
    }

    private DiskNode? HitTest(float x, float y)
    {
        if (_cachedLayout is null) return null;
        // Walk in reverse order to get the topmost (smallest/deepest) rect
        for (int i = _cachedLayout.Count - 1; i >= 0; i--)
        {
            var r = _cachedLayout[i];
            if (r.Bounds.Contains(x, y)) return r.Node;
        }
        return null;
    }

    // ── Tooltip ───────────────────────────────────────────────────────────────

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        var hit = HitTest((float)pos.X, (float)pos.Y);
        ToolTip.SetTip(this, hit is null ? null
            : $"{hit.Name}\n{hit.SizeDisplay} ({hit.PercentOfParent:F1}%)");
    }

    // ── Custom draw operation ─────────────────────────────────────────────────

    private sealed class TreemapDrawOp : ICustomDrawOperation
    {
        private readonly Rect              _bounds;
        private readonly List<TreemapRect> _rects;

        public TreemapDrawOp(Rect bounds, List<TreemapRect> rects)
        {
            _bounds = bounds;
            _rects  = rects;
        }

        public Rect Bounds => _bounds;
        public bool HitTest(Point p) => true;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            if (!context.TryGetFeature<ISkiaSharpApiLeaseFeature>(out var leaseFeature)) return;
            using var lease  = leaseFeature.Lease();
            var canvas = lease.SkCanvas;

            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, (float)_bounds.Width, (float)_bounds.Height));

            using var fillPaint = new SKPaint { IsAntialias = false };
            using var borderPaint = new SKPaint
            {
                IsAntialias = false,
                Style       = SKPaintStyle.Stroke,
                Color       = new SKColor(0, 0, 0, 80),
                StrokeWidth = 1f,
            };
            using var textPaint = new SKPaint
            {
                IsAntialias = true,
                Color       = SKColors.White,
                TextSize    = 11f,
            };

            foreach (var item in _rects)
            {
                var r   = item.Bounds;
                uint c  = item.Node.IsDirectory
                    ? FileTypeClassifier.GetFolderColor(item.Depth)
                    : FileTypeClassifier.GetCategoryColor(
                          FileTypeClassifier.Classify(item.Node.Extension));

                fillPaint.Color = new SKColor(c);
                canvas.DrawRect(r, fillPaint);

                if (r.Width > 6 && r.Height > 6)
                    canvas.DrawRect(r, borderPaint);

                // Draw label only if rect is large enough
                if (r.Width > 40 && r.Height > 16)
                {
                    textPaint.TextSize = Math.Clamp(Math.Min(r.Width / 6f, 13f), 8f, 13f);
                    var label = item.Node.Name;
                    float textW = textPaint.MeasureText(label);
                    if (textW > r.Width - 4)
                    {
                        // Truncate to fit
                        while (label.Length > 1 && textPaint.MeasureText(label + "\u2026") > r.Width - 4)
                            label = label[..^1];
                        label += "\u2026";
                    }
                    canvas.DrawText(label, r.Left + 2, r.Top + textPaint.TextSize + 2, textPaint);
                }
            }

            canvas.Restore();
        }
    }
}
