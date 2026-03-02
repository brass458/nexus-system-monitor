// Nexus System Monitor — Icon Generator
// Renders the Nexus Hub icon to .png, .ico, and .icns at all required sizes.
// Usage: dotnet run -- <output-dir>
//   Outputs: nexus-icon-16.png, nexus-icon-32.png, nexus-icon-48.png,
//            nexus-icon-64.png, nexus-icon-128.png, nexus-icon-256.png,
//            nexus-icon-512.png, nexus-icon-1024.png,
//            nexus-icon.ico, nexus-icon.icns

using SkiaSharp;

string outDir = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(outDir);

int[] allSizes = [16, 32, 48, 64, 128, 256, 512, 1024];

// ── Render all PNG sizes ──────────────────────────────────────────────────
var pngData = new Dictionary<int, byte[]>();
foreach (int size in allSizes)
{
    byte[] data = RenderPng(size);
    pngData[size] = data;
    string path = Path.Combine(outDir, $"nexus-icon-{size}.png");
    File.WriteAllBytes(path, data);
    Console.WriteLine($"  PNG {size,4}×{size,-4}  →  {path}");
}

// ── Write ICO (Windows: 16, 32, 48, 256) ────────────────────────────────
int[] icoSizes = [16, 32, 48, 256];
byte[] icoBytes = BuildIco(pngData, icoSizes);
string icoPath = Path.Combine(outDir, "nexus-icon.ico");
File.WriteAllBytes(icoPath, icoBytes);
Console.WriteLine($"  ICO         →  {icoPath}");

// ── Write ICNS (macOS: 16, 32, 64, 128, 256, 512, 1024) ─────────────────
int[] icnsSizes = [16, 32, 64, 128, 256, 512, 1024];
byte[] icnsBytes = BuildIcns(pngData, icnsSizes);
string icnsPath = Path.Combine(outDir, "nexus-icon.icns");
File.WriteAllBytes(icnsPath, icnsBytes);
Console.WriteLine($"  ICNS        →  {icnsPath}");

Console.WriteLine("Done.");

// ─────────────────────────────────────────────────────────────────────────
// Icon Design: "Nexus Hub"
//
// A modern dark-background icon featuring:
//  • A deep navy rounded-square background with radial gradient
//  • A central "hub" disc with bright blue radial gradient + glow
//  • 6 equidistant spoke-lines from center to rim nodes (circuit style)
//  • 6 bright outer nodes (circles) at rim positions
//  • A subtle outer ring connecting the nodes
//  • A faint inner hexagonal accent
// ─────────────────────────────────────────────────────────────────────────
static byte[] RenderPng(int size)
{
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
    SKCanvas canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent);

    float s = size;
    float cx = s * 0.5f;
    float cy = s * 0.5f;
    float cr = s * 0.21f;              // background corner radius

    // Proportion constants — all relative to `s` so they scale perfectly
    float rimR       = s * 0.345f;     // outer node ring radius
    float hubR       = s * 0.155f;     // central hub circle radius
    float nodeR      = s * 0.060f;     // outer node circle radius
    float glowR      = s * 0.30f;      // center glow spread
    float traceW     = s * 0.023f;     // spoke line width
    float ringW      = traceW * 0.55f; // outer ring stroke width
    float hexW       = traceW * 0.40f; // inner hexagon stroke

    // ── 1. Background ────────────────────────────────────────────────────
    using (var bgPaint = new SKPaint { IsAntialias = true })
    {
        bgPaint.Shader = SKShader.CreateRadialGradient(
            new SKPoint(cx, cy * 0.85f), s * 0.75f,
            new SKColor[] { new(0x18, 0x1C, 0x35), new(0x08, 0x09, 0x14) },
            null, SKShaderTileMode.Clamp);
        canvas.DrawRoundRect(new SKRoundRect(new SKRect(0, 0, s, s), cr, cr), bgPaint);
    }

    // ── 2. Inner hex accent (very subtle, only visible at large sizes) ────
    if (size >= 48)
    {
        using var hexPaint = new SKPaint
        {
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = hexW,
            Color       = new SKColor(0x00, 0x55, 0xAA, 0x40),
        };
        DrawRegularPolygon(canvas, cx, cy, rimR * 0.60f, 6, -MathF.PI / 2, hexPaint);
    }

    // ── 3. Outer ring (subtle, connects the 6 nodes) ─────────────────────
    using (var ringPaint = new SKPaint
    {
        IsAntialias = true,
        Style       = SKPaintStyle.Stroke,
        StrokeWidth = ringW,
        Color       = new SKColor(0x00, 0x77, 0xCC, 0x55),
    })
    {
        canvas.DrawCircle(cx, cy, rimR, ringPaint);
    }

    // ── 4. Spoke lines (circuit traces from center to each node) ─────────
    using (var tracePaint = new SKPaint
    {
        IsAntialias  = true,
        Style        = SKPaintStyle.Stroke,
        StrokeWidth  = traceW,
        StrokeCap    = SKStrokeCap.Round,
    })
    {
        for (int i = 0; i < 6; i++)
        {
            float angle = i * MathF.PI / 3f - MathF.PI / 2f;  // start at top
            float nx    = cx + rimR * MathF.Cos(angle);
            float ny    = cy + rimR * MathF.Sin(angle);

            // Gradient from center (brighter) to node (dimmer)
            tracePaint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(cx, cy), new SKPoint(nx, ny),
                new SKColor[] { new(0x00, 0x88, 0xFF, 0xCC), new(0x00, 0x55, 0xAA, 0x66) },
                null, SKShaderTileMode.Clamp);

            canvas.DrawLine(cx, cy, nx, ny, tracePaint);
        }
    }

    // ── 5. Center glow ────────────────────────────────────────────────────
    using (var glowPaint = new SKPaint { IsAntialias = true })
    {
        glowPaint.Shader = SKShader.CreateRadialGradient(
            new SKPoint(cx, cy), glowR,
            new SKColor[] { new(0x00, 0x88, 0xFF, 0x70), new(0x00, 0x44, 0xCC, 0x00) },
            null, SKShaderTileMode.Clamp);
        glowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, s * 0.04f);
        canvas.DrawCircle(cx, cy, glowR, glowPaint);
    }

    // ── 6. Outer nodes ─────────────────────────────────────────────────
    for (int i = 0; i < 6; i++)
    {
        float angle = i * MathF.PI / 3f - MathF.PI / 2f;
        float nx    = cx + rimR * MathF.Cos(angle);
        float ny    = cy + rimR * MathF.Sin(angle);

        // Node glow
        using (var nGlowPaint = new SKPaint { IsAntialias = true })
        {
            nGlowPaint.Shader = SKShader.CreateRadialGradient(
                new SKPoint(nx, ny), nodeR * 2.5f,
                new SKColor[] { new(0x00, 0xCC, 0xFF, 0x55), new(0x00, 0x66, 0xFF, 0x00) },
                null, SKShaderTileMode.Clamp);
            canvas.DrawCircle(nx, ny, nodeR * 2.5f, nGlowPaint);
        }

        // Node fill
        using var nodePaint = new SKPaint { IsAntialias = true };
        nodePaint.Shader = SKShader.CreateRadialGradient(
            new SKPoint(nx - nodeR * 0.25f, ny - nodeR * 0.25f), nodeR * 1.4f,
            new SKColor[] { new(0x55, 0xDD, 0xFF), new(0x00, 0x88, 0xEE) },
            null, SKShaderTileMode.Clamp);
        canvas.DrawCircle(nx, ny, nodeR, nodePaint);
    }

    // ── 7. Central hub ────────────────────────────────────────────────────
    using (var hubPaint = new SKPaint { IsAntialias = true })
    {
        hubPaint.Shader = SKShader.CreateRadialGradient(
            new SKPoint(cx - hubR * 0.22f, cy - hubR * 0.22f), hubR * 1.5f,
            new SKColor[] { new(0x66, 0xCC, 0xFF), new(0x00, 0x77, 0xFF) },
            null, SKShaderTileMode.Clamp);
        canvas.DrawCircle(cx, cy, hubR, hubPaint);
    }

    // Hub inner specular highlight
    using (var specPaint = new SKPaint { IsAntialias = true })
    {
        specPaint.Shader = SKShader.CreateRadialGradient(
            new SKPoint(cx - hubR * 0.28f, cy - hubR * 0.30f), hubR * 0.6f,
            new SKColor[] { new(0xFF, 0xFF, 0xFF, 0x88), new(0xFF, 0xFF, 0xFF, 0x00) },
            null, SKShaderTileMode.Clamp);
        canvas.DrawCircle(cx - hubR * 0.12f, cy - hubR * 0.12f, hubR * 0.55f, specPaint);
    }

    using var snapshot = surface.Snapshot();
    using var encoded  = snapshot.Encode(SKEncodedImageFormat.Png, 100);
    return encoded.ToArray();
}

// Draw a regular n-sided polygon centered at (cx,cy) with given radius and start angle
static void DrawRegularPolygon(SKCanvas canvas, float cx, float cy, float radius, int sides, float startAngle, SKPaint paint)
{
    using var path = new SKPath();
    for (int i = 0; i < sides; i++)
    {
        float angle = startAngle + i * 2f * MathF.PI / sides;
        float x = cx + radius * MathF.Cos(angle);
        float y = cy + radius * MathF.Sin(angle);
        if (i == 0) path.MoveTo(x, y);
        else        path.LineTo(x, y);
    }
    path.Close();
    canvas.DrawPath(path, paint);
}

// ── ICO format builder ────────────────────────────────────────────────────
// ICO: 6-byte header + N*16-byte directory + N*image-data
// Modern ICO stores PNG data directly for sizes >= 256; BMP for smaller.
static byte[] BuildIco(Dictionary<int, byte[]> pngsBySize, int[] sizes)
{
    // Use PNG encoding for all sizes (Windows Vista+ supports PNG in ICO)
    var images = sizes.Select(sz => pngsBySize[sz]).ToArray();

    using var ms = new MemoryStream();
    using var bw = new BinaryWriter(ms);

    // ICONDIR header
    bw.Write((ushort)0);          // reserved
    bw.Write((ushort)1);          // type: icon
    bw.Write((ushort)sizes.Length);

    // Calculate offsets — directory entries come after the header
    int dataOffset = 6 + sizes.Length * 16;
    int[] offsets = new int[sizes.Length];
    for (int i = 0; i < sizes.Length; i++)
    {
        offsets[i] = dataOffset;
        dataOffset += images[i].Length;
    }

    // ICONDIRENTRY for each image
    for (int i = 0; i < sizes.Length; i++)
    {
        int sz = sizes[i];
        bw.Write((byte)(sz >= 256 ? 0 : sz)); // width (0 = 256)
        bw.Write((byte)(sz >= 256 ? 0 : sz)); // height
        bw.Write((byte)0);                    // color count
        bw.Write((byte)0);                    // reserved
        bw.Write((ushort)1);                  // color planes
        bw.Write((ushort)32);                 // bits per pixel
        bw.Write((uint)images[i].Length);     // image data size
        bw.Write((uint)offsets[i]);           // offset to image data
    }

    // Image data
    foreach (var img in images)
        bw.Write(img);

    return ms.ToArray();
}

// ── ICNS format builder ───────────────────────────────────────────────────
// ICNS: 4-byte magic + 4-byte file size + chunks
// Each chunk: 4-byte type + 4-byte chunk-size (including 8-byte header) + PNG data
static byte[] BuildIcns(Dictionary<int, byte[]> pngsBySize, int[] sizes)
{
    // Map pixel sizes to ICNS type codes (PNG variants with 'f' suffix or direct)
    var typeMap = new Dictionary<int, string>
    {
        [16]   = "icp4",
        [32]   = "icp5",
        [64]   = "icp6",
        [128]  = "ic07",
        [256]  = "ic08",
        [512]  = "ic09",
        [1024] = "ic10",
    };

    using var ms = new MemoryStream();

    // Write all chunks to a temp buffer first so we know the total size
    using var chunkBuf = new MemoryStream();
    foreach (int sz in sizes)
    {
        if (!typeMap.TryGetValue(sz, out string? typeCode)) continue;
        byte[] png = pngsBySize[sz];
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(typeCode);
        uint chunkSize = (uint)(8 + png.Length);

        chunkBuf.Write(typeBytes);
        WriteUInt32BE(chunkBuf, chunkSize);
        chunkBuf.Write(png);
    }

    byte[] chunks = chunkBuf.ToArray();
    uint totalSize = (uint)(8 + chunks.Length);

    // ICNS header
    ms.Write(System.Text.Encoding.ASCII.GetBytes("icns"));
    WriteUInt32BE(ms, totalSize);
    ms.Write(chunks);

    return ms.ToArray();
}

static void WriteUInt32BE(Stream s, uint value)
{
    s.WriteByte((byte)(value >> 24));
    s.WriteByte((byte)(value >> 16));
    s.WriteByte((byte)(value >>  8));
    s.WriteByte((byte)(value      ));
}
