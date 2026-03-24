using System.Reactive.Subjects;
using FluentAssertions;
using Moq;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;
using NexusMonitor.Core.Storage;
using Xunit;
using MockFactory = NexusMonitor.Core.Tests.Helpers.MockFactory;
using TestMetricsDatabase = NexusMonitor.Core.Tests.Helpers.TestMetricsDatabase;

namespace NexusMonitor.Core.Tests;

/// <summary>
/// Integration tests for <see cref="MetricsStore"/>: event persistence, buffer
/// flush behaviour, historical queries, and lifecycle correctness.
/// All tests use a real SQLite-backed <see cref="TestMetricsDatabase"/>.
/// </summary>
public class MetricsStoreTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SystemMetrics MakeMetrics(double cpuPercent = 10.0, long memUsed = 1_000_000L)
    {
        return new SystemMetrics
        {
            Timestamp      = DateTime.UtcNow,
            Cpu            = new CpuMetrics { TotalPercent = cpuPercent },
            Memory         = new MemoryMetrics { UsedBytes = memUsed, TotalBytes = 8_000_000_000L },
            Disks          = new List<DiskMetrics>(),
            NetworkAdapters = new List<NetworkAdapterMetrics>(),
            Gpus           = new List<GpuMetrics>()
        };
    }

    private static ProcessInfo MakeProcess(int pid, string name, double cpu = 10.0, long mem = 100_000L)
    {
        return new ProcessInfo { Pid = pid, Name = name, CpuPercent = cpu, WorkingSetBytes = mem };
    }

    /// <summary>
    /// Builds the full wiring: subjects → mocks → store.
    /// Returns subjects so individual tests can emit ticks.
    /// </summary>
    private static (
        MetricsStore store,
        Subject<SystemMetrics> metricsSubject,
        Subject<IReadOnlyList<ProcessInfo>> processSubject,
        Subject<IReadOnlyList<NetworkConnection>> networkSubject,
        TestMetricsDatabase db)
    CreateStore(int writeBufferSize = 3)
    {
        var metricsSubject = new Subject<SystemMetrics>();
        var processSubject = new Subject<IReadOnlyList<ProcessInfo>>();
        var networkSubject = new Subject<IReadOnlyList<NetworkConnection>>();

        var mockMetrics = MockFactory.CreateMetricsProvider();
        var mockProcess = MockFactory.CreateProcessProvider();
        var mockNetwork = new Mock<INetworkConnectionsProvider>(MockBehavior.Loose);

        mockMetrics.Setup(p => p.GetMetricsStream(It.IsAny<TimeSpan>())).Returns(metricsSubject);
        mockProcess.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>())).Returns(processSubject);
        mockNetwork.Setup(p => p.GetConnectionStream(It.IsAny<TimeSpan>())).Returns(networkSubject);
        mockNetwork.Setup(p => p.SupportsPerConnectionThroughput).Returns(false);

        var db = new TestMetricsDatabase();
        var config = new MetricsStoreConfig { WriteBufferSize = writeBufferSize };
        var store = new MetricsStore(
            db.Database, config,
            mockMetrics.Object, mockProcess.Object, mockNetwork.Object,
            MockFactory.CreateLogger<MetricsStore>().Object);

        store.Start(TimeSpan.FromSeconds(1));

        return (store, metricsSubject, processSubject, networkSubject, db);
    }

    // Keep window under 2 hours total so GetSystemMetricsAsync uses the raw query path.
    private static DateTimeOffset Far(int minutesAgo = 30) =>
        DateTimeOffset.UtcNow.AddMinutes(-minutesAgo);

    private static DateTimeOffset Soon(int minutesAhead = 30) =>
        DateTimeOffset.UtcNow.AddMinutes(minutesAhead);

    // ── IEventWriter ──────────────────────────────────────────────────────────

    [Fact]
    public async Task InsertEventAsync_WritesToDb_CanBeReadBackByGetEventsAsync()
    {
        var (store, _, _, _, db) = CreateStore();
        using var _ = db;
        using var __ = store;

        await store.InsertEventAsync("cpu_high", 1, null, null, null, null);

        var events = await store.GetEventsAsync(Far(), Soon());
        events.Should().HaveCount(1);
        events[0].EventType.Should().Be("cpu_high");
    }

    [Fact]
    public async Task InsertEventAsync_AllFieldsPersistedCorrectly()
    {
        var (store, _, _, _, db) = CreateStore();
        using var _ = db;
        using var __ = store;

        await store.InsertEventAsync(
            eventType:    "process_spike",
            severity:     2,
            metricName:   "cpu_percent",
            metricValue:  95.5,
            threshold:    80.0,
            description:  "CPU spike detected",
            metadataJson: "{\"pid\":1234}");

        var events = await store.GetEventsAsync(Far(), Soon());
        events.Should().HaveCount(1);

        var ev = events[0];
        ev.EventType.Should().Be("process_spike");
        ev.Severity.Should().Be(2);
        ev.MetricName.Should().Be("cpu_percent");
        ev.MetricValue.Should().BeApproximately(95.5, 0.001);
        ev.Threshold.Should().BeApproximately(80.0, 0.001);
        ev.Description.Should().Be("CPU spike detected");
        ev.MetadataJson.Should().Be("{\"pid\":1234}");
    }

    [Fact]
    public async Task InsertEventAsync_NullableFields_NullsStoredAndReadBack()
    {
        var (store, _, _, _, db) = CreateStore();
        using var _ = db;
        using var __ = store;

        await store.InsertEventAsync("mem_high", 0, null, null, null, null, null);

        var events = await store.GetEventsAsync(Far(), Soon());
        events.Should().HaveCount(1);

        var ev = events[0];
        ev.MetricName.Should().BeNull();
        ev.MetricValue.Should().BeNull();
        ev.Threshold.Should().BeNull();
        ev.Description.Should().BeNull();
        ev.MetadataJson.Should().BeNull();
    }

    [Fact]
    public async Task GetEventsAsync_FilterByType_ReturnsOnlyMatchingEvents()
    {
        var (store, _, _, _, db) = CreateStore();
        using var _ = db;
        using var __ = store;

        await store.InsertEventAsync("cpu_high", 1, null, null, null, "cpu event");
        await store.InsertEventAsync("mem_high", 1, null, null, null, "mem event");
        await store.InsertEventAsync("cpu_high", 2, null, null, null, "another cpu");

        var cpuEvents = await store.GetEventsAsync(Far(), Soon(), eventType: "cpu_high");
        cpuEvents.Should().HaveCount(2);
        cpuEvents.Should().AllSatisfy(e => e.EventType.Should().Be("cpu_high"));
    }

    [Fact]
    public async Task GetEventsAsync_EmptyDb_ReturnsEmpty()
    {
        var (store, _, _, _, db) = CreateStore();
        using var _ = db;
        using var __ = store;

        var events = await store.GetEventsAsync(Far(), Soon());

        events.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEventsAsync_TimeRangeFilters_OnlyReturnsEventsInRange()
    {
        var (store, _, _, _, db) = CreateStore();
        using var _ = db;
        using var __ = store;

        // Insert an event; it will be timestamped UtcNow inside InsertEventAsync.
        await store.InsertEventAsync("cpu_high", 1, null, null, null, "in range");

        // Query a window from 10s ago to now (captures the event).
        var inRange = await store.GetEventsAsync(
            DateTimeOffset.UtcNow.AddSeconds(-10),
            DateTimeOffset.UtcNow.AddSeconds(1));
        inRange.Should().HaveCount(1);

        // Query a window entirely in the past (before the event).
        var outOfRange = await store.GetEventsAsync(
            DateTimeOffset.UtcNow.AddHours(-2),
            DateTimeOffset.UtcNow.AddHours(-1));
        outOfRange.Should().BeEmpty();
    }

    // ── Stop() flushes buffer ─────────────────────────────────────────────────

    [Fact]
    public async Task Stop_WithBufferedMetrics_FlushesBeforeClose()
    {
        // WriteBufferSize=3 — emit only 2 so auto-flush doesn't fire.
        var (store, metricsSubject, _, _, db) = CreateStore(writeBufferSize: 3);
        using var _ = db;

        metricsSubject.OnNext(MakeMetrics(cpuPercent: 11.0));
        metricsSubject.OnNext(MakeMetrics(cpuPercent: 22.0));

        store.Stop();

        var rows = await store.GetSystemMetricsAsync(Far(), Soon());
        rows.Should().HaveCount(2);

        store.Dispose();
    }

    [Fact]
    public async Task Stop_WithBufferedProcesses_FlushesProcessData()
    {
        var (store, _, processSubject, _, db) = CreateStore(writeBufferSize: 3);
        using var _ = db;

        processSubject.OnNext(new List<ProcessInfo>
        {
            MakeProcess(100, "alpha", cpu: 50.0),
            MakeProcess(200, "beta",  cpu: 30.0)
        });

        store.Stop();

        // alpha should be in process_snapshots
        var history = await store.GetProcessHistoryAsync("alpha", Far(), Soon());
        history.Should().HaveCount(1);
        history[0].Name.Should().Be("alpha");

        store.Dispose();
    }

    // ── Buffer size triggers auto-flush ───────────────────────────────────────

    [Fact]
    public async Task WriteBufferSize_WhenReached_AutoFlushes()
    {
        // Emit exactly WriteBufferSize ticks — flush fires inside OnMetricsTick.
        var (store, metricsSubject, _, _, db) = CreateStore(writeBufferSize: 3);
        using var _ = db;
        using var __ = store;

        metricsSubject.OnNext(MakeMetrics(cpuPercent: 10.0));
        metricsSubject.OnNext(MakeMetrics(cpuPercent: 20.0));
        metricsSubject.OnNext(MakeMetrics(cpuPercent: 30.0)); // this one should trigger flush

        var rows = await store.GetSystemMetricsAsync(Far(), Soon());
        rows.Should().HaveCount(3);
    }

    // ── IMetricsReader — system metrics ───────────────────────────────────────

    [Fact]
    public async Task GetSystemMetricsAsync_EmptyDb_ReturnsEmpty()
    {
        var (store, _, _, _, db) = CreateStore();
        using var _ = db;
        using var __ = store;

        var rows = await store.GetSystemMetricsAsync(Far(), Soon());

        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSystemMetricsAsync_ShortRange_ReturnsRawDataWithCorrectValues()
    {
        var (store, metricsSubject, _, _, db) = CreateStore(writeBufferSize: 3);
        using var _ = db;

        metricsSubject.OnNext(MakeMetrics(cpuPercent: 45.0, memUsed: 2_000_000L));
        metricsSubject.OnNext(MakeMetrics(cpuPercent: 55.0, memUsed: 3_000_000L));
        metricsSubject.OnNext(MakeMetrics(cpuPercent: 65.0, memUsed: 4_000_000L));

        store.Stop(); // also flushes if not yet auto-flushed

        // <2h range → raw data path
        var rows = await store.GetSystemMetricsAsync(Far(1), Soon(1));
        rows.Should().HaveCount(3);

        var cpuValues = rows.Select(r => r.CpuPercent).OrderBy(v => v).ToList();
        cpuValues.Should().ContainInOrder(45.0, 55.0, 65.0);

        store.Dispose();
    }

    // ── IMetricsReader — process history ─────────────────────────────────────

    [Fact]
    public async Task GetProcessHistoryAsync_ReturnsMatchingProcessByName()
    {
        var (store, _, processSubject, _, db) = CreateStore(writeBufferSize: 1);
        using var _ = db;

        processSubject.OnNext(new List<ProcessInfo>
        {
            MakeProcess(42, "target_proc", cpu: 77.0, mem: 500_000L)
        });

        store.Stop();

        var history = await store.GetProcessHistoryAsync("target_proc", Far(), Soon());
        history.Should().HaveCount(1);
        history[0].Name.Should().Be("target_proc");
        history[0].Pid.Should().Be(42);
        history[0].CpuPercent.Should().BeApproximately(77.0, 0.001);
        history[0].MemBytes.Should().Be(500_000L);

        store.Dispose();
    }

    [Fact]
    public async Task GetProcessHistoryAsync_NoMatchingName_ReturnsEmpty()
    {
        var (store, _, processSubject, _, db) = CreateStore(writeBufferSize: 1);
        using var _ = db;

        processSubject.OnNext(new List<ProcessInfo>
        {
            MakeProcess(1, "some_proc", cpu: 5.0)
        });

        store.Stop();

        var history = await store.GetProcessHistoryAsync("nonexistent_proc", Far(), Soon());
        history.Should().BeEmpty();

        store.Dispose();
    }

    // ── IMetricsReader — GetDataRange ─────────────────────────────────────────

    [Fact]
    public async Task GetDataRangeAsync_EmptyDb_ReturnsNow()
    {
        var (store, _, _, _, db) = CreateStore();
        using var _ = db;
        using var __ = store;

        var before = DateTimeOffset.UtcNow.AddSeconds(-5);
        var (oldest, newest) = await store.GetDataRangeAsync();
        var after = DateTimeOffset.UtcNow.AddSeconds(5);

        oldest.Should().BeOnOrAfter(before);
        oldest.Should().BeOnOrBefore(after);
        newest.Should().BeOnOrAfter(before);
        newest.Should().BeOnOrBefore(after);
    }

    [Fact]
    public async Task GetDataRangeAsync_WithData_ReturnsCorrectBounds()
    {
        var (store, metricsSubject, _, _, db) = CreateStore(writeBufferSize: 2);
        using var _ = db;

        metricsSubject.OnNext(MakeMetrics());
        metricsSubject.OnNext(MakeMetrics());

        store.Stop();

        var (oldest, newest) = await store.GetDataRangeAsync();

        oldest.Should().BeOnOrBefore(newest);
        // Both should be recent (within the last minute)
        oldest.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-1));
        newest.Should().BeBefore(DateTimeOffset.UtcNow.AddSeconds(5));

        store.Dispose();
    }

    // ── IMetricsReader — top process summaries ────────────────────────────────

    [Fact]
    public async Task GetTopProcessSummariesAsync_ReturnsSortedByAvgCpuDesc()
    {
        var (store, _, processSubject, _, db) = CreateStore(writeBufferSize: 1);
        using var _ = db;

        // Emit two ticks with the same processes so averages are stable
        var tick = new List<ProcessInfo>
        {
            MakeProcess(1, "low_cpu",  cpu: 5.0,  mem: 100_000L),
            MakeProcess(2, "high_cpu", cpu: 80.0, mem: 200_000L),
            MakeProcess(3, "mid_cpu",  cpu: 40.0, mem: 150_000L),
        };
        processSubject.OnNext(tick);

        store.Stop();

        var summaries = await store.GetTopProcessSummariesAsync(Far(), Soon(), topN: 10);
        summaries.Should().HaveCount(3);

        // Must be sorted by avg_cpu descending
        summaries[0].Name.Should().Be("high_cpu");
        summaries[1].Name.Should().Be("mid_cpu");
        summaries[2].Name.Should().Be("low_cpu");

        store.Dispose();
    }

    [Fact]
    public async Task GetTopProcessSummariesAsync_TopN_LimitsResults()
    {
        var (store, _, processSubject, _, db) = CreateStore(writeBufferSize: 1);
        using var _ = db;

        var tick = new List<ProcessInfo>
        {
            MakeProcess(1, "proc_a", cpu: 10.0),
            MakeProcess(2, "proc_b", cpu: 20.0),
            MakeProcess(3, "proc_c", cpu: 30.0),
            MakeProcess(4, "proc_d", cpu: 40.0),
            MakeProcess(5, "proc_e", cpu: 50.0),
        };
        processSubject.OnNext(tick);

        store.Stop();

        var summaries = await store.GetTopProcessSummariesAsync(Far(), Soon(), topN: 3);
        summaries.Should().HaveCount(3);

        // Top 3 by CPU desc
        summaries[0].Name.Should().Be("proc_e");
        summaries[1].Name.Should().Be("proc_d");
        summaries[2].Name.Should().Be("proc_c");

        store.Dispose();
    }

    // ── GetDatabaseSizeBytes ──────────────────────────────────────────────────

    [Fact]
    public void GetDatabaseSizeBytes_AfterFlush_ReturnsPositiveValue()
    {
        var (store, metricsSubject, _, _, db) = CreateStore(writeBufferSize: 1);
        using var _ = db;

        metricsSubject.OnNext(MakeMetrics());

        store.Stop();

        var size = store.GetDatabaseSizeBytes();
        size.Should().BeGreaterThan(0);

        store.Dispose();
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_IsIdempotent_SecondDisposeNoException()
    {
        var (store, _, _, _, db) = CreateStore();
        db.Dispose();

        store.Dispose();

        var act = () => store.Dispose();
        act.Should().NotThrow();
    }
}
