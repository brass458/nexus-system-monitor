using System.Reactive.Linq;
using System.Reactive.Subjects;
using NexusMonitor.Core.Services;

namespace NexusMonitor.Platform.Linux;

/// <summary>
/// Linux wallpaper service: tries GNOME, KDE, Xfce desktop environment strategies.
/// Falls back to <see cref="WallpaperInfo.Default"/> (mid-range luminance) if none work.
/// Polls every 30 seconds for changes.
/// </summary>
public sealed class LinuxWallpaperService : IWallpaperService, IDisposable
{
    private readonly Subject<WallpaperInfo> _subject   = new();
    private readonly System.Timers.Timer    _pollTimer;
    private          WallpaperInfo          _last;

    public IObservable<WallpaperInfo> WallpaperChanged => _subject.AsObservable();

    public LinuxWallpaperService()
    {
        _last = GetCurrentWallpaper();
        _pollTimer = new System.Timers.Timer(30_000) { AutoReset = true };
        _pollTimer.Elapsed += (_, _) => CheckForChange();
        _pollTimer.Start();
    }

    public WallpaperInfo GetCurrentWallpaper()
    {
        return TryGnome()
            ?? TryKde()
            ?? TryXfce()
            ?? WallpaperInfo.Default;
    }

    private static WallpaperInfo? TryGnome()
    {
        try
        {
            var output = RunProcess("gsettings",
                "get org.gnome.desktop.background picture-uri");
            if (string.IsNullOrWhiteSpace(output)) return null;
            var path = output.Trim().Trim('\'', '"');
            if (path.StartsWith("file://")) path = new Uri(path).LocalPath;
            return File.Exists(path) ? WallpaperInfo.FromFile(path) : null;
        }
        catch { return null; }
    }

    private static WallpaperInfo? TryKde()
    {
        try
        {
            // KDE stores wallpaper in plasma config; path varies by version
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configPath = Path.Combine(homeDir, ".config", "plasma-org.kde.plasma.desktop-appletsrc");
            if (!File.Exists(configPath)) return null;

            foreach (var line in File.ReadLines(configPath))
            {
                if (line.StartsWith("Image=", StringComparison.OrdinalIgnoreCase))
                {
                    var path = line[6..].Trim();
                    if (path.StartsWith("file://")) path = new Uri(path).LocalPath;
                    if (File.Exists(path)) return WallpaperInfo.FromFile(path);
                }
            }
        }
        catch { /* fall through */ }
        return null;
    }

    private static WallpaperInfo? TryXfce()
    {
        try
        {
            var output = RunProcess("xfconf-query",
                "-c xfce4-desktop -p /backdrop/screen0/monitor0/image-path");
            if (string.IsNullOrWhiteSpace(output)) return null;
            var path = output.Trim();
            return File.Exists(path) ? WallpaperInfo.FromFile(path) : null;
        }
        catch { return null; }
    }

    private static string? RunProcess(string exe, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName               = exe,
            Arguments              = args,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            CreateNoWindow         = true,
        };
        var proc = System.Diagnostics.Process.Start(psi);
        if (proc is null) return null;
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(2000);
        return output;
    }

    private void CheckForChange()
    {
        var current = GetCurrentWallpaper();
        if (current.FilePath != _last.FilePath)
        {
            _last = current;
            _subject.OnNext(current);
        }
    }

    public void Dispose()
    {
        _pollTimer.Dispose();
        _subject.Dispose();
    }
}
