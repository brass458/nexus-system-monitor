using System.Reactive.Subjects;
using FluentAssertions;
using NexusMonitor.Core.Health;
using NexusMonitor.Core.Models;
using Xunit;

namespace NexusMonitor.Core.Tests;

/// <summary>
/// Unit tests for <see cref="HealthSnapshotPersistenceService"/>.
/// Uses a <see cref="Subject{T}"/> for the health stream and a tracked
/// Func delegate for the write callback — no real SQLite needed.
/// </summary>
public class HealthSnapshotPersistenceServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SystemHealthSnapshot MakeSnapshot(double score = 80.0) =>
        new SystemHealthSnapshot
        {
            OverallScore  = score,
            OverallHealth = HealthLevel.Good,
            Cpu           = new SubsystemHealth { Score = score },
            Memory        = new SubsystemHealth { Score = score },
            Disk          = new SubsystemHealth { Score = score },
            Gpu           = new SubsystemHealth { Score = score },
            Timestamp     = DateTime.UtcNow,
        };

    /// <summary>
    /// Creates a service wired to a controllable subject and a write counter.
    /// </summary>
    private static (
        HealthSnapshotPersistenceService svc,
        Subject<SystemHealthSnapshot>    subject,
        List<SystemHealthSnapshot>       written)
    Create()
    {
        var subject = new Subject<SystemHealthSnapshot>();
        var written = new List<SystemHealthSnapshot>();
        var svc     = new HealthSnapshotPersistenceService(
            subject,
            snap => { written.Add(snap); return Task.CompletedTask; });
        return (svc, subject, written);
    }

    /// <summary>Emits <paramref name="count"/> distinct snapshots into the subject.</summary>
    private static void Emit(Subject<SystemHealthSnapshot> subject, int count)
    {
        for (var i = 0; i < count; i++)
            subject.OnNext(MakeSnapshot(i));
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Start_WritesSnapshot_AfterDownsampleInterval()
    {
        var (svc, subject, written) = Create();

        svc.Start();
        Emit(subject, HealthSnapshotPersistenceService.DownsampleEvery);

        written.Should().HaveCount(1,
            "exactly one snapshot should be persisted after {0} ticks",
            HealthSnapshotPersistenceService.DownsampleEvery);

        svc.Dispose();
    }

    [Fact]
    public void Start_DoesNotWrite_BeforeInterval()
    {
        var (svc, subject, written) = Create();

        svc.Start();
        Emit(subject, HealthSnapshotPersistenceService.DownsampleEvery - 1);

        written.Should().BeEmpty("no snapshot should be persisted before the downsample threshold");

        svc.Dispose();
    }

    [Fact]
    public void Start_WritesMultipleSnapshots_AcrossMultipleIntervals()
    {
        var (svc, subject, written) = Create();

        svc.Start();
        Emit(subject, HealthSnapshotPersistenceService.DownsampleEvery * 3);

        written.Should().HaveCount(3,
            "one snapshot per complete {0}-tick interval",
            HealthSnapshotPersistenceService.DownsampleEvery);

        svc.Dispose();
    }

    [Fact]
    public void Stop_StopsWriting()
    {
        var (svc, subject, written) = Create();

        svc.Start();
        Emit(subject, HealthSnapshotPersistenceService.DownsampleEvery); // triggers 1 write
        svc.Stop();
        Emit(subject, HealthSnapshotPersistenceService.DownsampleEvery); // should be ignored

        written.Should().HaveCount(1,
            "writes after Stop() should be discarded");
    }

    [Fact]
    public void Start_IsIdempotent()
    {
        var (svc, subject, written) = Create();

        svc.Start();
        svc.Start(); // second call should be a no-op

        Emit(subject, HealthSnapshotPersistenceService.DownsampleEvery);

        written.Should().HaveCount(1,
            "calling Start() twice must not double the subscription");

        svc.Dispose();
    }

    [Fact]
    public void Dispose_StopsSubscription()
    {
        var (svc, subject, written) = Create();

        svc.Start();
        Emit(subject, HealthSnapshotPersistenceService.DownsampleEvery); // triggers 1 write
        svc.Dispose();
        Emit(subject, HealthSnapshotPersistenceService.DownsampleEvery); // should be ignored after dispose

        written.Should().HaveCount(1,
            "no further writes should occur after Dispose()");
    }

    [Fact]
    public void WrittenSnapshot_IsTheSnapshotAtTheDownsampleBoundary()
    {
        var (svc, subject, written) = Create();
        svc.Start();

        // Emit N-1 irrelevant snapshots, then emit the boundary one with a known score
        Emit(subject, HealthSnapshotPersistenceService.DownsampleEvery - 1);
        var boundary = MakeSnapshot(score: 42.0);
        subject.OnNext(boundary);

        written.Should().ContainSingle()
               .Which.OverallScore.Should().Be(42.0,
                   "the snapshot at tick {0} should be the one persisted",
                   HealthSnapshotPersistenceService.DownsampleEvery);

        svc.Dispose();
    }
}
