using NexusMonitor.Core.Storage;

namespace NexusMonitor.Core.Tests.Helpers;

/// <summary>
/// Wraps MetricsDatabase with a temp file path so tests get a real, fully-initialized
/// schema without touching production data. Deletes the file on Dispose().
/// </summary>
public sealed class TestMetricsDatabase : IDisposable
{
    private readonly string _path;
    private bool _disposed;

    public MetricsDatabase Database { get; }

    public TestMetricsDatabase()
    {
        _path = Path.Combine(Path.GetTempPath(), $"nexus_test_{Guid.NewGuid():N}.db");
        Database = new MetricsDatabase(_path);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Database.Dispose();
        TryDelete(_path);
        TryDelete(_path + "-wal");
        TryDelete(_path + "-shm");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }
}
