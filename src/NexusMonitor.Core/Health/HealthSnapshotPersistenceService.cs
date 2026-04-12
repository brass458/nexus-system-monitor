namespace NexusMonitor.Core.Health;

/// <summary>
/// Subscribes to a health snapshot stream and persists a downsampled snapshot to
/// storage every <see cref="DownsampleEvery"/> ticks (default 15 × 2 s = 30 s).
/// </summary>
public sealed class HealthSnapshotPersistenceService : IDisposable
{
    private readonly IObservable<SystemHealthSnapshot>          _healthStream;
    private readonly Func<SystemHealthSnapshot, Task>           _writeSnapshot;

    private IDisposable? _subscription;
    private int          _tickCount;

    /// <summary>
    /// Number of stream ticks between persisted snapshots.
    /// At the default 2 s tick interval this equals 30 s.
    /// </summary>
    public const int DownsampleEvery = 15;

    // ── Production constructor (wired by DI) ──────────────────────────────

    /// <summary>
    /// Creates the service from the resolved <see cref="SystemHealthService"/> stream
    /// and the <see cref="NexusMonitor.Core.Storage.MetricsStore"/> write method.
    /// </summary>
    public HealthSnapshotPersistenceService(
        SystemHealthService                              healthService,
        NexusMonitor.Core.Storage.MetricsStore          metricsStore)
        : this(healthService.HealthStream, metricsStore.WriteHealthSnapshotAsync)
    { }

    // ── Testable constructor ──────────────────────────────────────────────

    /// <summary>
    /// Creates the service with explicit stream and write-delegate injection.
    /// Used by unit tests to supply a <see cref="System.Reactive.Subjects.Subject{T}"/>
    /// and a mock write delegate without constructing real infrastructure objects.
    /// </summary>
    public HealthSnapshotPersistenceService(
        IObservable<SystemHealthSnapshot> healthStream,
        Func<SystemHealthSnapshot, Task>  writeSnapshot)
    {
        _healthStream  = healthStream  ?? throw new ArgumentNullException(nameof(healthStream));
        _writeSnapshot = writeSnapshot ?? throw new ArgumentNullException(nameof(writeSnapshot));
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    /// <summary>
    /// Subscribes to the health stream. Idempotent — calling Start() a second time
    /// while already running has no effect.
    /// </summary>
    public void Start()
    {
        if (_subscription != null) return;

        _subscription = _healthStream.Subscribe(OnTick);
    }

    /// <summary>
    /// Unsubscribes from the health stream. Safe to call when not started.
    /// </summary>
    public void Stop()
    {
        _subscription?.Dispose();
        _subscription = null;
    }

    public void Dispose() => Stop();

    // ── Internal ──────────────────────────────────────────────────────────

    private void OnTick(SystemHealthSnapshot snapshot)
    {
        var tick = Interlocked.Increment(ref _tickCount); // First write at tick DownsampleEvery (30 s); increment is atomic for thread-safety
        if (tick % DownsampleEvery == 0)
            _ = _writeSnapshot(snapshot);
    }
}
