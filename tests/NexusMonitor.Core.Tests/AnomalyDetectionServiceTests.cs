using System.Reactive.Subjects;
using FluentAssertions;
using Moq;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;
using NexusMonitor.Core.Storage;
using NexusMonitor.Core.Tests.Helpers;
using Xunit;

namespace NexusMonitor.Core.Tests;

public class AnomalyDetectionServiceTests
{
    // ── Helpers ────────────────────────────────────────────────────────────

    private static SystemMetrics MakeMetrics(
        double cpuPercent = 10.0,
        long   memUsed    = 1_000_000L,
        long   memTotal   = 8_000_000_000L)
        => new SystemMetrics
        {
            Timestamp      = DateTime.UtcNow,
            Cpu            = new CpuMetrics { TotalPercent = cpuPercent },
            Memory         = new MemoryMetrics { UsedBytes = memUsed, TotalBytes = memTotal },
            Disks          = new List<DiskMetrics>(),
            NetworkAdapters= new List<NetworkAdapterMetrics>(),
            Gpus           = new List<GpuMetrics>()
        };

    private (AnomalyDetectionService svc,
             Subject<SystemMetrics>   metricsSubject,
             Mock<IEventWriter>       writer)
        CreateService(AnomalyDetectionConfig? config = null)
    {
        config ??= new AnomalyDetectionConfig { Enabled = true, CooldownSeconds = 0 };

        var metricsSubject = new Subject<SystemMetrics>();
        var processSubject = new Subject<IReadOnlyList<ProcessInfo>>();
        var networkSubject = new Subject<IReadOnlyList<NetworkConnection>>();

        var mockMetrics  = Helpers.MockFactory.CreateMetricsProvider();
        var mockProcess  = Helpers.MockFactory.CreateProcessProvider();
        var mockNetwork  = new Mock<INetworkConnectionsProvider>();

        mockMetrics.Setup(p => p.GetMetricsStream(It.IsAny<TimeSpan>()))
                   .Returns(metricsSubject);
        mockProcess.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                   .Returns(processSubject);
        mockNetwork.Setup(p => p.GetConnectionStream(It.IsAny<TimeSpan>()))
                   .Returns(networkSubject);

        var writer = new Mock<IEventWriter>();
        writer.Setup(w => w.InsertEventAsync(
                    It.IsAny<string>(),  It.IsAny<int>(),
                    It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<double?>(),
                    It.IsAny<string?>(), It.IsAny<string?>()))
              .Returns(Task.CompletedTask);

        var svc = new AnomalyDetectionService(
            config, writer.Object,
            mockMetrics.Object, mockProcess.Object, mockNetwork.Object);
        svc.Start();

        return (svc, metricsSubject, writer);
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Start_WhenDisabled_DoesNotSubscribe()
    {
        var config = new AnomalyDetectionConfig { Enabled = false };
        var (svc, metricsSubject, writer) = CreateService(config);

        metricsSubject.OnNext(MakeMetrics(cpuPercent: 99.0));
        await Task.Delay(100);

        writer.Verify(w => w.InsertEventAsync(
            It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<double?>(),
            It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);

        svc.Dispose();
    }

    [Fact]
    public async Task Start_WhenEnabled_SubscribesToStreams()
    {
        var (svc, metricsSubject, writer) = CreateService();

        metricsSubject.OnNext(MakeMetrics(cpuPercent: 96.0));
        await Task.Delay(100);

        writer.Verify(w => w.InsertEventAsync(
            EventType.CpuHigh, EventSeverity.Critical,
            It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<double?>(),
            It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Once);

        svc.Dispose();
    }

    [Fact]
    public async Task Stop_DisposesSubscriptions()
    {
        var (svc, metricsSubject, writer) = CreateService();

        svc.Stop();

        metricsSubject.OnNext(MakeMetrics(cpuPercent: 96.0));
        await Task.Delay(100);

        writer.Verify(w => w.InsertEventAsync(
            It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<double?>(),
            It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);

        svc.Dispose();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var (svc, _, _) = CreateService();

        var act = () =>
        {
            svc.Dispose();
            svc.Dispose();
        };

        act.Should().NotThrow();
    }

    // ── CPU ceiling ────────────────────────────────────────────────────────

    [Fact]
    public async Task CpuCeiling_WhenHit_FiresCriticalEvent()
    {
        var (svc, metricsSubject, writer) = CreateService();

        metricsSubject.OnNext(MakeMetrics(cpuPercent: 96.0)); // > default ceiling 95
        await Task.Delay(100);

        writer.Verify(w => w.InsertEventAsync(
            EventType.CpuHigh, EventSeverity.Critical,
            It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<double?>(),
            It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Once);

        svc.Dispose();
    }

    [Fact]
    public async Task CpuCeiling_WhenHit_AnomalyDetectedFires()
    {
        var (svc, metricsSubject, _) = CreateService();

        StoredEvent? received = null;
        svc.AnomalyDetected.Subscribe(e => received = e);

        metricsSubject.OnNext(MakeMetrics(cpuPercent: 96.0));
        await Task.Delay(100);

        received.Should().NotBeNull();
        received!.EventType.Should().Be(EventType.CpuHigh);
        received.Severity.Should().Be(EventSeverity.Critical);

        svc.Dispose();
    }

    [Fact]
    public async Task CpuCeiling_BelowCeiling_NoEvent()
    {
        var (svc, metricsSubject, writer) = CreateService();

        metricsSubject.OnNext(MakeMetrics(cpuPercent: 94.0)); // below 95 ceiling, no warmup for sigma
        await Task.Delay(100);

        writer.Verify(w => w.InsertEventAsync(
            It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<double?>(),
            It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);

        svc.Dispose();
    }

    // ── Cooldown ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Cooldown_PreventsRepeatEvents()
    {
        var config = new AnomalyDetectionConfig
        {
            Enabled          = true,
            CooldownSeconds  = 100  // long cooldown — second event should be suppressed
        };
        var (svc, metricsSubject, writer) = CreateService(config);

        metricsSubject.OnNext(MakeMetrics(cpuPercent: 96.0));
        await Task.Delay(100);
        metricsSubject.OnNext(MakeMetrics(cpuPercent: 96.0));
        await Task.Delay(100);

        writer.Verify(w => w.InsertEventAsync(
            EventType.CpuHigh, It.IsAny<int>(),
            It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<double?>(),
            It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Once);

        svc.Dispose();
    }

    [Fact]
    public async Task Cooldown_WhenExpired_AllowsSecondEvent()
    {
        var config = new AnomalyDetectionConfig
        {
            Enabled         = true,
            CooldownSeconds = 0  // no cooldown — both events should fire
        };
        var (svc, metricsSubject, writer) = CreateService(config);

        metricsSubject.OnNext(MakeMetrics(cpuPercent: 96.0));
        await Task.Delay(100);
        metricsSubject.OnNext(MakeMetrics(cpuPercent: 96.0));
        await Task.Delay(100);

        writer.Verify(w => w.InsertEventAsync(
            EventType.CpuHigh, It.IsAny<int>(),
            It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<double?>(),
            It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Exactly(2));

        svc.Dispose();
    }

    // ── Memory ceiling ─────────────────────────────────────────────────────

    [Fact]
    public async Task MemCeiling_WhenHit_FiresCriticalEvent()
    {
        var (svc, metricsSubject, writer) = CreateService();

        // 96% usage — UsedBytes/TotalBytes * 100 > default ceiling 95
        long total = 8_000_000_000L;
        long used  = (long)(total * 0.96);
        metricsSubject.OnNext(MakeMetrics(memUsed: used, memTotal: total));
        await Task.Delay(100);

        writer.Verify(w => w.InsertEventAsync(
            EventType.MemHigh, EventSeverity.Critical,
            It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<double?>(),
            It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Once);

        svc.Dispose();
    }

    // ── AnomalyDetected observable ─────────────────────────────────────────

    [Fact]
    public async Task AnomalyDetected_MultipleSubscribers_AllReceiveEvent()
    {
        var (svc, metricsSubject, _) = CreateService();

        var received1 = new List<StoredEvent>();
        var received2 = new List<StoredEvent>();
        svc.AnomalyDetected.Subscribe(e => received1.Add(e));
        svc.AnomalyDetected.Subscribe(e => received2.Add(e));

        metricsSubject.OnNext(MakeMetrics(cpuPercent: 96.0));
        await Task.Delay(100);

        received1.Should().HaveCount(1);
        received2.Should().HaveCount(1);

        svc.Dispose();
    }

    // ── ApplySensitivity ───────────────────────────────────────────────────

    [Fact]
    public void ApplySensitivity_Low_SetsHigherSigma()
    {
        var config = new AnomalyDetectionConfig();
        config.ApplySensitivity("Low");

        config.SigmaCpu.Should().Be(3.5);
    }

    [Fact]
    public void ApplySensitivity_High_SetsLowerSigma()
    {
        var config = new AnomalyDetectionConfig();
        config.ApplySensitivity("High");

        config.SigmaCpu.Should().Be(1.5);
    }

    [Fact]
    public void ApplySensitivity_Net_AlwaysHigherThanCpu()
    {
        foreach (var preset in new[] { "Low", "Medium", "High" })
        {
            var config = new AnomalyDetectionConfig();
            config.ApplySensitivity(preset);

            config.SigmaNet.Should().Be(config.SigmaCpu + 0.5,
                because: $"SigmaNet should always be SigmaCpu+0.5 after '{preset}' preset");
        }
    }

    // ── Enabled flag ───────────────────────────────────────────────────────

    [Fact]
    public async Task Disabled_CeilingHit_NoEvent()
    {
        var config = new AnomalyDetectionConfig { Enabled = false };
        var (svc, metricsSubject, writer) = CreateService(config);

        metricsSubject.OnNext(MakeMetrics(cpuPercent: 99.0));
        await Task.Delay(100);

        writer.Verify(w => w.InsertEventAsync(
            It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<double?>(),
            It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);

        svc.Dispose();
    }

    // ── Dispose / observable completion ───────────────────────────────────

    [Fact]
    public async Task Dispose_CompletesAnomalyDetectedObservable()
    {
        var (svc, metricsSubject, writer) = CreateService();

        // Confirm events fire before dispose
        metricsSubject.OnNext(MakeMetrics(cpuPercent: 96.0));
        await Task.Delay(100);
        writer.Verify(w => w.InsertEventAsync(
            It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<double?>(),
            It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Once);

        svc.Dispose();
        await Task.Delay(50);

        // After dispose, further subject emissions should not produce new events
        // (subscriptions are disposed, so the metrics subject is no longer wired up)
        metricsSubject.OnNext(MakeMetrics(cpuPercent: 96.0));
        await Task.Delay(100);

        // Still only one call — no new event after dispose
        writer.Verify(w => w.InsertEventAsync(
            It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<double?>(),
            It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Once);
    }
}
