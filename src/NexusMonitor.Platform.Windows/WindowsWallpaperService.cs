using System.Reactive.Linq;
using System.Reactive.Subjects;
using Microsoft.Win32;
using NexusMonitor.Core.Services;

namespace NexusMonitor.Platform.Windows;

/// <summary>
/// Windows wallpaper service: reads from HKCU registry and watches for changes
/// via FileSystemWatcher (file changes) + 30-second polling (slideshow / color).
/// </summary>
public sealed class WindowsWallpaperService : IWallpaperService, IDisposable
{
    private readonly Subject<WallpaperInfo>   _subject  = new();
    private readonly System.Timers.Timer      _pollTimer;
    private          FileSystemWatcher?       _watcher;
    private          string?                  _watchedFile;
    private          WallpaperInfo            _last;

    public IObservable<WallpaperInfo> WallpaperChanged => _subject.AsObservable();

    public WindowsWallpaperService()
    {
        _last = GetCurrentWallpaper();
        _pollTimer = new System.Timers.Timer(30_000) { AutoReset = true };
        _pollTimer.Elapsed += (_, _) => CheckForChange();
        _pollTimer.Start();
        WatchFile(_last.FilePath);
    }

    public WallpaperInfo GetCurrentWallpaper()
    {
        try
        {
            // Try image file path first
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            var filePath = key?.GetValue("Wallpaper") as string;
            if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                return WallpaperInfo.FromFile(filePath);

            // Fall back to solid background color
            using var colorKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors");
            var colorStr = colorKey?.GetValue("Background") as string;
            if (colorStr is not null)
            {
                var parts = colorStr.Trim().Split(' ');
                if (parts.Length >= 3
                    && byte.TryParse(parts[0], out byte r)
                    && byte.TryParse(parts[1], out byte g)
                    && byte.TryParse(parts[2], out byte b))
                    return WallpaperInfo.FromColor(r, g, b);
            }
        }
        catch { /* fall through */ }

        return WallpaperInfo.Default;
    }

    private void CheckForChange()
    {
        var current = GetCurrentWallpaper();
        if (current.FilePath != _last.FilePath
         || current.SolidR   != _last.SolidR
         || current.SolidG   != _last.SolidG
         || current.SolidB   != _last.SolidB)
        {
            _last = current;
            WatchFile(current.FilePath);
            _subject.OnNext(current);
        }
    }

    private void WatchFile(string? path)
    {
        _watcher?.Dispose();
        _watcher = null;
        _watchedFile = path;

        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        try
        {
            var dir  = Path.GetDirectoryName(path)!;
            var file = Path.GetFileName(path);
            _watcher = new FileSystemWatcher(dir, file)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, _) => CheckForChange();
            _watcher.Renamed += (_, _) => CheckForChange();
        }
        catch { /* watcher is optional */ }
    }

    public void Dispose()
    {
        _pollTimer.Dispose();
        _watcher?.Dispose();
        _subject.Dispose();
    }
}
