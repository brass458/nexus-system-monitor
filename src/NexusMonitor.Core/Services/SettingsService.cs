using System.Text.Json;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Services;

public class SettingsService : IDisposable
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NexusMonitor", "settings.json");

    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    private readonly object _saveLock = new();
    private Timer? _debounceTimer;

    public AppSettings Current { get; private set; } = new();

    public SettingsService() => Load();

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
        }
        catch { Current = new(); }
    }

    /// <summary>
    /// Saves settings with 250 ms debounce to coalesce rapid slider updates.
    /// Thread-safe: concurrent calls are serialized via _saveLock.
    /// </summary>
    public void Save()
    {
        // Restart the debounce timer on every call; the actual write happens 250 ms after the last call.
        lock (_saveLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ => WriteToDisk(), null,
                dueTime: TimeSpan.FromMilliseconds(250),
                period:  Timeout.InfiniteTimeSpan);
        }
    }

    private void WriteToDisk()
    {
        try
        {
            string json;
            lock (_saveLock)
                json = JsonSerializer.Serialize(Current, _opts);

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            // Write to a temp file then rename to prevent partial writes corrupting the file
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
        catch { }
    }

    public void Dispose()
    {
        lock (_saveLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
        WriteToDisk();
    }
}
