using NexusMonitor.Core.Services;
using NexusMonitor.UI.Services;

namespace NexusMonitor.UI.Services;

/// <summary>
/// Orchestrates smart glass opacity adjustments based on wallpaper luminance.
///
/// Luminance mapping (ITU-R BT.601 value, 0=black → 1=white):
///   0.0 → minAlpha 0x80 (50%) — dark wallpaper allows very transparent glass
///   1.0 → minAlpha 0xE0 (87%) — bright wallpaper forces more opaque glass
///
/// Fires <see cref="LuminanceChanged"/> when the computed min alpha changes.
/// </summary>
public sealed class GlassAdaptiveService : IDisposable
{
    private readonly IWallpaperService _wallpaperService;
    private          IDisposable?      _subscription;
    private          byte              _currentMinAlpha = 0xA0;

    /// <summary>Fires when wallpaper luminance changes. Arg = new min-alpha floor (0x80–0xE0).</summary>
    public event Action<byte>? LuminanceChanged;

    /// <summary>The most recently computed minimum alpha floor.</summary>
    public byte CurrentMinAlpha => _currentMinAlpha;

    public GlassAdaptiveService(IWallpaperService wallpaperService)
    {
        _wallpaperService = wallpaperService;
    }

    /// <summary>Start observing wallpaper changes. Computes luminance immediately.</summary>
    public void Start()
    {
        // Compute immediately from the current wallpaper
        var current = _wallpaperService.GetCurrentWallpaper();
        UpdateFromWallpaper(current);

        // Subscribe to future changes
        _subscription = _wallpaperService.WallpaperChanged.Subscribe(UpdateFromWallpaper);
    }

    public void Stop()
    {
        _subscription?.Dispose();
        _subscription = null;
    }

    private void UpdateFromWallpaper(WallpaperInfo info)
    {
        float luminance = WallpaperLuminanceAnalyzer.Analyze(info);
        byte  minAlpha  = MapLuminanceToAlpha(luminance);

        if (minAlpha == _currentMinAlpha) return;
        _currentMinAlpha = minAlpha;
        LuminanceChanged?.Invoke(minAlpha);
    }

    /// <summary>
    /// Linear interpolation: luminance 0.0 → 0x80 (50%), luminance 1.0 → 0xE0 (87%).
    /// </summary>
    private static byte MapLuminanceToAlpha(float luminance)
    {
        const float minA = 0x80;   // 128
        const float maxA = 0xE0;   // 224
        float clamped = Math.Clamp(luminance, 0f, 1f);
        return (byte)Math.Round(minA + clamped * (maxA - minA));
    }

    public void Dispose()
    {
        Stop();
    }
}
