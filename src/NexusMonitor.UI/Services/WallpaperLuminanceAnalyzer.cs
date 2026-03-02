using NexusMonitor.Core.Services;
using SkiaSharp;

namespace NexusMonitor.UI.Services;

/// <summary>
/// Computes ITU-R BT.601 weighted luminance from a <see cref="WallpaperInfo"/>.
/// Returns a value in [0.0, 1.0] where 0 = black and 1 = white.
/// Uses SkiaSharp to decode image files; falls back to 0.5 on any error.
/// </summary>
public static class WallpaperLuminanceAnalyzer
{
    private const int SampleSize = 100; // downscale target for fast analysis

    /// <summary>
    /// Compute luminance for the given wallpaper.
    /// Returns 0.5 (mid-range) on failure so glass opacity remains unchanged.
    /// </summary>
    public static float Analyze(WallpaperInfo info)
    {
        try
        {
            return info.IsSolidColor
                ? ComputeLuminance(info.SolidR / 255f, info.SolidG / 255f, info.SolidB / 255f)
                : AnalyzeFile(info.FilePath!);
        }
        catch
        {
            return 0.5f;
        }
    }

    private static float AnalyzeFile(string path)
    {
        if (!File.Exists(path)) return 0.5f;

        using var bmp = LoadAndDownscale(path);
        if (bmp is null) return 0.5f;

        long rSum = 0, gSum = 0, bSum = 0;
        int  pixelCount = 0;

        for (int y = 0; y < bmp.Height; y++)
        for (int x = 0; x < bmp.Width;  x++)
        {
            var px = bmp.GetPixel(x, y);
            rSum += px.Red;
            gSum += px.Green;
            bSum += px.Blue;
            pixelCount++;
        }

        if (pixelCount == 0) return 0.5f;

        float r = (float)(rSum / (255.0 * pixelCount));
        float g = (float)(gSum / (255.0 * pixelCount));
        float b = (float)(bSum / (255.0 * pixelCount));
        return ComputeLuminance(r, g, b);
    }

    private static SKBitmap? LoadAndDownscale(string path)
    {
        using var stream = File.OpenRead(path);
        using var codec  = SKCodec.Create(stream);
        if (codec is null) return null;

        var info = new SKImageInfo(SampleSize, SampleSize, SKColorType.Rgb888x);
        var bmp  = new SKBitmap(info);
        codec.GetPixels(info, bmp.GetPixels());
        return bmp;
    }

    /// <summary>ITU-R BT.601 weighted luminance from linear [0,1] RGB.</summary>
    private static float ComputeLuminance(float r, float g, float b)
        => 0.299f * r + 0.587f * g + 0.114f * b;
}
