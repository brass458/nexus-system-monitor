using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace NexusMonitor.UI.Controls;

/// <summary>
/// A circular HSV colour-picker disc with an integrated brightness strip.
/// The top portion renders the hue-saturation wheel; the bottom strip lets
/// the user control the Value (brightness) component.
/// Exposes <see cref="SelectedColor"/> as a bindable <see cref="Avalonia.Media.Color"/>.
/// </summary>
public class ColorWheelControl : Avalonia.Controls.Control
{
    // ── Styled Properties ─────────────────────────────────────────────────────

    public static readonly StyledProperty<Color> SelectedColorProperty =
        AvaloniaProperty.Register<ColorWheelControl, Color>(
            nameof(SelectedColor), Color.Parse("#0A84FF"));

    public Color SelectedColor
    {
        get => GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    // ── Internal HSV state ────────────────────────────────────────────────────

    private float _h = 210f, _s = 1f, _v = 1f;
    private bool  _updating;

    private enum HitZone { None, Disc, Strip }

    // ── Static constructor (property change handlers) ─────────────────────────

    static ColorWheelControl()
    {
        AffectsRender<ColorWheelControl>(SelectedColorProperty);

        SelectedColorProperty.Changed.AddClassHandler<ColorWheelControl>((c, e) =>
        {
            if (c._updating) return;
            var col = (Color)e.NewValue!;
            RgbToHsv(col.R, col.G, col.B, out c._h, out c._s, out c._v);
            c.InvalidateVisual();
        });
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        context.Custom(new ColorWheelDrawOp(bounds, _h, _s, _v));
    }

    // ── Pointer interaction ───────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            // Check zone BEFORE capturing — prevents capturing when the user
            // clicks in the blank space outside the disc and strip, which would
            // steal pointer events from all other controls on the page.
            var pos = e.GetPosition(this);
            if (HitZoneAt(pos) == HitZone.None) return;
            e.Pointer.Capture(this);
            HandlePointer(pos, true);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured == this)
            HandlePointer(e.GetPosition(this), false);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
    }

    private void HandlePointer(Point pos, bool firstPress)
    {
        var zone = HitZoneAt(pos);
        if (zone == HitZone.None && firstPress) return;

        if (zone == HitZone.Disc)
        {
            ComputeDiscGeometry(out float cx, out float cy, out float radius);
            float dx    = (float)pos.X - cx;
            float dy    = (float)pos.Y - cy;
            float dist  = MathF.Sqrt(dx * dx + dy * dy);
            _h = (MathF.Atan2(dy, dx) * 180f / MathF.PI + 360f) % 360f;
            _s = Math.Clamp(dist / radius, 0f, 1f);
        }
        else if (zone == HitZone.Strip)
        {
            ComputeStripGeometry(out float sx, out float sw);
            _v = Math.Clamp(((float)pos.X - sx) / sw, 0f, 1f);
        }

        _updating = true;
        SelectedColor = HsvToAvaloniaColor(_h, _s, _v);
        _updating = false;
        InvalidateVisual();
    }

    // ── Geometry helpers ──────────────────────────────────────────────────────

    private const float StripHeight = 14f;
    private const float StripGap    = 8f;
    private const float DiscPad     = 4f;

    private void ComputeDiscGeometry(out float cx, out float cy, out float radius)
    {
        float discAreaH = (float)Bounds.Height - StripGap - StripHeight - DiscPad;
        float size      = Math.Min((float)Bounds.Width, discAreaH);
        radius = size / 2f - DiscPad;
        cx     = (float)Bounds.Width  / 2f;
        cy     = size / 2f;
    }

    private void ComputeStripGeometry(out float sx, out float sw)
    {
        float discAreaH = (float)Bounds.Height - StripGap - StripHeight - DiscPad;
        float size      = Math.Min((float)Bounds.Width, discAreaH);
        float stripTop  = size + StripGap;
        sx = DiscPad;
        sw = (float)Bounds.Width - DiscPad * 2;
        _ = stripTop; // used by draw op; here we only need sx/sw
    }

    private HitZone HitZoneAt(Point pos)
    {
        ComputeDiscGeometry(out float cx, out float cy, out float radius);
        float dx   = (float)pos.X - cx;
        float dy   = (float)pos.Y - cy;
        if (MathF.Sqrt(dx * dx + dy * dy) <= radius + 4f)
            return HitZone.Disc;

        float discAreaH = (float)Bounds.Height - StripGap - StripHeight - DiscPad;
        float size      = Math.Min((float)Bounds.Width, discAreaH);
        float stripTop  = size + StripGap;
        if (pos.Y >= stripTop && pos.Y <= stripTop + StripHeight)
            return HitZone.Strip;

        return HitZone.None;
    }

    // ── HSV ↔ RGB helpers ─────────────────────────────────────────────────────

    internal static SKColor HsvToSkColor(float h, float s, float v)
    {
        if (s <= 0f) { byte gg = (byte)(v * 255); return new SKColor(gg, gg, gg); }
        float hh = (h >= 360f ? 0f : h) / 60f;
        int   ii = (int)hh;
        float ff = hh - ii;
        float p  = v * (1f - s);
        float q  = v * (1f - s * ff);
        float t  = v * (1f - s * (1f - ff));
        float r, g, b;
        switch (ii)
        {
            case 0:  r = v; g = t; b = p; break;
            case 1:  r = q; g = v; b = p; break;
            case 2:  r = p; g = v; b = t; break;
            case 3:  r = p; g = q; b = v; break;
            case 4:  r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
        return new SKColor((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    private static Color HsvToAvaloniaColor(float h, float s, float v)
    {
        var sk = HsvToSkColor(h, s, v);
        return Color.FromRgb(sk.Red, sk.Green, sk.Blue);
    }

    internal static void RgbToHsv(byte r, byte g, byte b, out float h, out float s, out float v)
    {
        float rf = r / 255f, gf = g / 255f, bf = b / 255f;
        float max = Math.Max(rf, Math.Max(gf, bf));
        float min = Math.Min(rf, Math.Min(gf, bf));
        float d   = max - min;
        v = max;
        s = max > 0f ? d / max : 0f;
        if (d <= 0f) { h = 0f; return; }
        if      (max == rf) h = 60f * (((gf - bf) / d + 6f) % 6f);
        else if (max == gf) h = 60f * ((bf - rf) / d + 2f);
        else                h = 60f * ((rf - gf) / d + 4f);
        if (h < 0f) h += 360f;
    }

    // ── Custom draw operation ─────────────────────────────────────────────────

    private sealed class ColorWheelDrawOp : ICustomDrawOperation
    {
        private readonly Rect  _bounds;
        private readonly float _h, _s, _v;

        public ColorWheelDrawOp(Rect bounds, float h, float s, float v)
        { _bounds = bounds; _h = h; _s = s; _v = v; }

        public Rect Bounds => _bounds;
        public bool HitTest(Point p) => true;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            if (!context.TryGetFeature<ISkiaSharpApiLeaseFeature>(out var lease_)) return;
            using var lease  = lease_.Lease();
            var       canvas = lease.SkCanvas;
            canvas.Save();

            // ── Disc geometry ──────────────────────────────────────────────
            float discAreaH = (float)_bounds.Height - StripGap - StripHeight - DiscPad;
            float size      = Math.Min((float)_bounds.Width, discAreaH);
            float cx        = (float)_bounds.Width / 2f;
            float cy        = size / 2f;
            float radius    = size / 2f - DiscPad;

            var center = new SKPoint(cx, cy);

            // ── 1. Clip to disc ──────────────────────────────────────────────
            using var discPath = new SKPath();
            discPath.AddCircle(cx, cy, radius);
            canvas.Save();
            canvas.ClipPath(discPath, antialias: true);

            // ── 2. Hue sweep gradient ────────────────────────────────────────
            var hueColors = new SKColor[]
            {
                new SKColor(255,   0,   0),  // Red    0°
                new SKColor(255, 255,   0),  // Yellow 60°
                new SKColor(  0, 255,   0),  // Green  120°
                new SKColor(  0, 255, 255),  // Cyan   180°
                new SKColor(  0,   0, 255),  // Blue   240°
                new SKColor(255,   0, 255),  // Magenta 300°
                new SKColor(255,   0,   0),  // Red    360°
            };
            // SKShader.CreateSweepGradient starts at 3-o'clock (0°=East) by default
            // We rotate -90° so red is at top (like most colour pickers)
            using var hueShader = SKShader.CreateSweepGradient(
                center, hueColors, null,
                SKShaderTileMode.Clamp,
                -90f, 270f);
            using var huePaint = new SKPaint { Shader = hueShader, IsAntialias = true };
            canvas.DrawPaint(huePaint);

            // ── 3. Saturation radial overlay: white center → transparent edge ──
            var satColors = new SKColor[] { SKColors.White, new SKColor(255, 255, 255, 0) };
            using var satShader = SKShader.CreateRadialGradient(
                center, radius, satColors, null, SKShaderTileMode.Clamp);
            using var satPaint = new SKPaint { Shader = satShader, IsAntialias = true };
            canvas.DrawPaint(satPaint);

            // ── 4. Value/brightness overlay: uniform black dim ──────────────
            if (_v < 1f)
            {
                byte blackA = (byte)(255 * (1f - _v));
                using var valPaint = new SKPaint
                    { Color = new SKColor(0, 0, 0, blackA), IsAntialias = true };
                canvas.DrawPaint(valPaint);
            }
            canvas.Restore(); // end disc clip

            // ── 5. Disc border ring ──────────────────────────────────────────
            using var ringPaint = new SKPaint
            {
                Color       = new SKColor(255, 255, 255, 50),
                Style       = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f,
                IsAntialias = true,
            };
            canvas.DrawCircle(cx, cy, radius, ringPaint);

            // ── 6. Selector dot on disc ──────────────────────────────────────
            float dotAngle = _h * MathF.PI / 180f;
            float dotDist  = _s * radius;
            float dotX     = cx + MathF.Cos(dotAngle) * dotDist;
            float dotY     = cy + MathF.Sin(dotAngle) * dotDist;

            var selectedSk = HsvToSkColor(_h, _s, _v);
            using var dotShadow = new SKPaint
                { Color = new SKColor(0, 0, 0, 100), IsAntialias = true };
            using var dotOuter  = new SKPaint
                { Color = SKColors.White, IsAntialias = true };
            using var dotInner  = new SKPaint
                { Color = selectedSk, IsAntialias = true };
            canvas.DrawCircle(dotX + 1, dotY + 1, 8f, dotShadow);
            canvas.DrawCircle(dotX, dotY, 8f, dotOuter);
            canvas.DrawCircle(dotX, dotY, 6f, dotInner);

            // ── 7. Brightness strip ──────────────────────────────────────────
            float stripTop  = size + StripGap;
            float stripLeft = DiscPad;
            float stripW    = (float)_bounds.Width - DiscPad * 2;
            float stripBot  = stripTop + StripHeight;
            float stripR    = StripHeight / 2f; // rounded ends

            var stripRect = new SKRoundRect(
                new SKRect(stripLeft, stripTop, stripLeft + stripW, stripBot), stripR, stripR);
            canvas.Save();
            canvas.ClipRoundRect(stripRect, antialias: true);

            // Gradient: black → full-saturation hue color
            SKColor fullColor = HsvToSkColor(_h, _s, 1f);
            var stripColors = new SKColor[] { SKColors.Black, fullColor };
            using var stripShader = SKShader.CreateLinearGradient(
                new SKPoint(stripLeft, 0),
                new SKPoint(stripLeft + stripW, 0),
                stripColors, null, SKShaderTileMode.Clamp);
            using var stripPaint = new SKPaint { Shader = stripShader };
            canvas.DrawRoundRect(stripRect, stripPaint);
            canvas.Restore();

            // Strip border
            using var stripBorder = new SKPaint
            {
                Color       = new SKColor(255, 255, 255, 50),
                Style       = SKPaintStyle.Stroke,
                StrokeWidth = 1f,
                IsAntialias = true,
            };
            canvas.DrawRoundRect(stripRect, stripBorder);

            // Strip indicator (vertical line at current brightness)
            float indicX = stripLeft + _v * stripW;
            using var indicPaint = new SKPaint
                { Color = SKColors.White, IsAntialias = true, StrokeWidth = 2f };
            canvas.DrawLine(indicX, stripTop + 1, indicX, stripBot - 1, indicPaint);

            canvas.Restore(); // outer save
        }
    }
}
