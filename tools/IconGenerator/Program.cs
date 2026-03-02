// Nexus System Monitor — Icon Generator
// Loads a source PNG and resizes it to all required sizes for .png, .ico, and .icns.
// Usage: dotnet run -- <output-dir> [source-png]
//   Outputs: nexus-icon-16.png, nexus-icon-32.png, nexus-icon-48.png,
//            nexus-icon-64.png, nexus-icon-128.png, nexus-icon-256.png,
//            nexus-icon-512.png, nexus-icon-1024.png,
//            nexus-icon.ico, nexus-icon.icns

using SkiaSharp;

string outDir        = args.Length > 0 ? args[0] : ".";
string sourcePngPath = args.Length > 1 ? args[1] : "Nexus System Monitor.png";

Directory.CreateDirectory(outDir);

if (!File.Exists(sourcePngPath))
    throw new FileNotFoundException($"Source PNG not found: {sourcePngPath}");

using var sourceBitmap = SKBitmap.Decode(sourcePngPath)
    ?? throw new InvalidOperationException($"Failed to decode source PNG: {sourcePngPath}");

int[] allSizes = [16, 32, 48, 64, 128, 256, 512, 1024];

// ── Render all PNG sizes ──────────────────────────────────────────────────
var pngData = new Dictionary<int, byte[]>();
foreach (int size in allSizes)
{
    byte[] data = RenderPng(size, sourceBitmap);
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

// ── Resize source PNG to target size using high-quality filtering ─────────
static byte[] RenderPng(int size, SKBitmap source)
{
    using var resized = source.Resize(new SKImageInfo(size, size), SKFilterQuality.High);
    if (resized is null) throw new InvalidOperationException($"Failed to resize to {size}×{size}");
    using var image   = SKImage.FromBitmap(resized);
    using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
    return encoded.ToArray();
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
