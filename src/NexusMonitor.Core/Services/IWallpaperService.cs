namespace NexusMonitor.Core.Services;

/// <summary>
/// Information about the current desktop wallpaper.
/// Either a file path (image) or a solid RGB color is set.
/// </summary>
public sealed class WallpaperInfo
{
    /// <summary>Absolute path to the wallpaper image file, or null for solid color.</summary>
    public string? FilePath   { get; init; }

    /// <summary>Solid color R/G/B (0-255 each), used when FilePath is null.</summary>
    public byte SolidR { get; init; } = 128;
    public byte SolidG { get; init; } = 128;
    public byte SolidB { get; init; } = 128;

    /// <summary>True when the wallpaper is a solid color rather than an image file.</summary>
    public bool IsSolidColor => FilePath is null;

    public static WallpaperInfo FromFile(string path) => new() { FilePath = path };
    public static WallpaperInfo FromColor(byte r, byte g, byte b) =>
        new() { SolidR = r, SolidG = g, SolidB = b };
    public static WallpaperInfo Default => new();
}

/// <summary>
/// Platform abstraction for reading the current desktop wallpaper and observing changes.
/// </summary>
public interface IWallpaperService
{
    /// <summary>Get the current wallpaper synchronously (best-effort).</summary>
    WallpaperInfo GetCurrentWallpaper();

    /// <summary>Observable that fires whenever the wallpaper changes.</summary>
    IObservable<WallpaperInfo> WallpaperChanged { get; }
}

/// <summary>
/// No-op fallback that returns mid-range luminance defaults.
/// Used when no platform wallpaper service is available.
/// </summary>
public sealed class NullWallpaperService : IWallpaperService
{
    public WallpaperInfo GetCurrentWallpaper() => WallpaperInfo.Default;
    public IObservable<WallpaperInfo> WallpaperChanged =>
        System.Reactive.Linq.Observable.Never<WallpaperInfo>();
}
