using System.Reactive.Linq;
using System.Reactive.Subjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Alerts;
using NexusMonitor.Core.Models;
using NexusMonitor.Core.Services;
using Xunit;

namespace NexusMonitor.Core.Tests;

public class AlertsServiceTests
{
    // ── Helpers ────────────────────────────────────────────────────────────

    private (AlertsService svc, Subject<SystemMetrics> subject, Mock<INotificationService> notifMock)
        CreateService(AppSettings? settings = null)
    {
        var subject = new Subject<SystemMetrics>();
        var metricsProvider = new Mock<ISystemMetricsProvider>();
        metricsProvider.Setup(m => m.GetMetricsStream(It.IsAny<TimeSpan>()))
                       .Returns(subject.AsObservable());
        var notifMock = new Mock<INotificationService>();
        var svc = new AlertsService(metricsProvider.Object, settings ?? new AppSettings(), notifMock.Object,
                                    NullLogger<AlertsService>.Instance);
        return (svc, subject, notifMock);
    }

    private static AlertRule MakeRule(
        AlertMetric metric      = AlertMetric.CpuPercent,
        double      threshold   = 80,
        int         sustainSec  = 0,
        int         cooldownSec = 0) =>
        new AlertRule
        {
            Metric      = metric,
            Threshold   = threshold,
            SustainSec  = sustainSec,
            CooldownSec = cooldownSec,
            IsEnabled   = true,
        };

    private static SystemMetrics MetricsWithCpu(double cpu) =>
        new SystemMetrics { Cpu = new CpuMetrics { TotalPercent = cpu } };

    private static SystemMetrics MetricsWithRam(double ramPercent)
    {
        long total = 8_000_000_000L;
        long used  = (long)(total * ramPercent / 100.0);
        return new SystemMetrics { Memory = new MemoryMetrics { TotalBytes = total, UsedBytes = used } };
    }

    private static SystemMetrics MetricsWithGpu(double gpuPercent) =>
        new SystemMetrics { Gpus = new List<GpuMetrics> { new GpuMetrics { UsagePercent = gpuPercent } } };

    private static SystemMetrics MetricsWithCpuTemp(double tempCelsius) =>
        new SystemMetrics { Cpu = new CpuMetrics { TemperatureCelsius = tempCelsius } };

    // ── Lifecycle ──────────────────────────────────────────────────────────

    [Fact]
    public void Start_SetsIsRunning_True()
    {
        var (svc, _, _) = CreateService();

        svc.Start();

        svc.IsRunning.Should().BeTrue();
        svc.Dispose();
    }

    [Fact]
    public void Stop_SetsIsRunning_False()
    {
        var (svc, _, _) = CreateService();

        svc.Start();
        svc.Stop();

        svc.IsRunning.Should().BeFalse();
        svc.Dispose();
    }

    [Fact]
    public void Start_CalledTwice_DoesNotDuplicate()
    {
        var settings = new AppSettings
        {
            AlertRules = new List<AlertRule> { MakeRule() }
        };
        var (svc, subject, _) = CreateService(settings);

        svc.Start();
        svc.Start(); // second call should be no-op

        var received = new List<AlertEvent>();
        svc.Events.Subscribe(e => received.Add(e));

        subject.OnNext(MetricsWithCpu(95.0));

        received.Should().HaveCount(1);
        svc.Dispose();
    }

    [Fact]
    public void Dispose_StopsService()
    {
        var settings = new AppSettings
        {
            AlertRules = new List<AlertRule> { MakeRule() }
        };
        var (svc, subject, _) = CreateService(settings);
        svc.Start();

        svc.Dispose();

        var received = new List<AlertEvent>();
        // After dispose, subscribe would fail silently — we just check IsRunning
        svc.IsRunning.Should().BeFalse();
    }

    // ── Rule evaluation ────────────────────────────────────────────────────

    [Fact]
    public void OnTick_NoRules_NoEvents()
    {
        var settings = new AppSettings { AlertRules = new List<AlertRule>() };
        var (svc, subject, _) = CreateService(settings);
        svc.Start();

        var received = new List<AlertEvent>();
        svc.Events.Subscribe(e => received.Add(e));

        subject.OnNext(MetricsWithCpu(99.0));

        received.Should().BeEmpty();
        svc.Dispose();
    }

    [Fact]
    public void OnTick_DisabledRule_NoEvent()
    {
        var rule = MakeRule();
        rule.IsEnabled = false;
        var settings = new AppSettings { AlertRules = new List<AlertRule> { rule } };
        var (svc, subject, _) = CreateService(settings);
        svc.Start();

        var received = new List<AlertEvent>();
        svc.Events.Subscribe(e => received.Add(e));

        subject.OnNext(MetricsWithCpu(95.0));

        received.Should().BeEmpty();
        svc.Dispose();
    }

    [Fact]
    public void OnTick_ValueBelowThreshold_NoEvent()
    {
        var settings = new AppSettings
        {
            AlertRules = new List<AlertRule> { MakeRule(threshold: 80) }
        };
        var (svc, subject, _) = CreateService(settings);
        svc.Start();

        var received = new List<AlertEvent>();
        svc.Events.Subscribe(e => received.Add(e));

        subject.OnNext(MetricsWithCpu(70.0)); // below threshold of 80

        received.Should().BeEmpty();
        svc.Dispose();
    }

    [Fact]
    public void OnTick_ValueAboveThreshold_SustainNotMet_NoEvent()
    {
        // SustainSec=10: first tick sets _firstSeen to now, sustainedSeconds ≈ 0 < 10
        var settings = new AppSettings
        {
            AlertRules = new List<AlertRule> { MakeRule(threshold: 80, sustainSec: 10, cooldownSec: 0) }
        };
        var (svc, subject, _) = CreateService(settings);
        svc.Start();

        var received = new List<AlertEvent>();
        svc.Events.Subscribe(e => received.Add(e));

        subject.OnNext(MetricsWithCpu(95.0)); // above threshold but sustain not met yet

        received.Should().BeEmpty();
        svc.Dispose();
    }

    [Fact]
    public void OnTick_ValueAboveThreshold_SustainMet_FiresEvent()
    {
        // SustainSec=0: sustainedSeconds >= 0 is always true on first tick
        var settings = new AppSettings
        {
            AlertRules = new List<AlertRule> { MakeRule(threshold: 80, sustainSec: 0, cooldownSec: 0) }
        };
        var (svc, subject, _) = CreateService(settings);
        svc.Start();

        var received = new List<AlertEvent>();
        svc.Events.Subscribe(e => received.Add(e));

        subject.OnNext(MetricsWithCpu(95.0));

        received.Should().HaveCount(1);
        received[0].Rule.Metric.Should().Be(AlertMetric.CpuPercent);
        received[0].Value.Should().Be(95.0);
        svc.Dispose();
    }

    [Fact]
    public void OnTick_AlertCount_IncrementedOnFire()
    {
        var settings = new AppSettings
        {
            AlertRules = new List<AlertRule> { MakeRule(threshold: 80, sustainSec: 0, cooldownSec: 0) }
        };
        var (svc, subject, _) = CreateService(settings);
        svc.Start();

        svc.AlertCount.Should().Be(0);

        subject.OnNext(MetricsWithCpu(95.0));

        svc.AlertCount.Should().Be(1);
        svc.Dispose();
    }

    [Fact]
    public void OnTick_Cooldown_PreventsDuplicateFire()
    {
        // CooldownSec=60: first tick fires, second tick within 60s is suppressed
        var settings = new AppSettings
        {
            AlertRules = new List<AlertRule> { MakeRule(threshold: 80, sustainSec: 0, cooldownSec: 60) }
        };
        var (svc, subject, _) = CreateService(settings);
        svc.Start();

        var received = new List<AlertEvent>();
        svc.Events.Subscribe(e => received.Add(e));

        subject.OnNext(MetricsWithCpu(95.0)); // fires
        subject.OnNext(MetricsWithCpu(95.0)); // suppressed by 60s cooldown

        received.Should().HaveCount(1);
        svc.Dispose();
    }

    [Fact]
    public void OnTick_ValueDropsBelowThreshold_ResetsSustain()
    {
        // Emit cpu=95 (SustainSec=0, CooldownSec=0): fires (event 1)
        // Emit cpu=50: drops below threshold, resets _firstSeen
        // Emit cpu=95 again: new _firstSeen, SustainSec=0 fires immediately (event 2)
        var settings = new AppSettings
        {
            AlertRules = new List<AlertRule> { MakeRule(threshold: 80, sustainSec: 0, cooldownSec: 0) }
        };
        var (svc, subject, _) = CreateService(settings);
        svc.Start();

        var received = new List<AlertEvent>();
        svc.Events.Subscribe(e => received.Add(e));

        subject.OnNext(MetricsWithCpu(95.0)); // fires → event 1
        subject.OnNext(MetricsWithCpu(50.0)); // drops below, resets _firstSeen; no event
        subject.OnNext(MetricsWithCpu(95.0)); // new _firstSeen, fires immediately → event 2

        received.Should().HaveCount(2);
        svc.Dispose();
    }

    // ── Metric routing ─────────────────────────────────────────────────────

    [Fact]
    public void OnTick_RamMetric_UsesMemoryUsedPercent()
    {
        var settings = new AppSettings
        {
            AlertRules = new List<AlertRule> { MakeRule(metric: AlertMetric.RamPercent, threshold: 80, sustainSec: 0, cooldownSec: 0) }
        };
        var (svc, subject, _) = CreateService(settings);
        svc.Start();

        var received = new List<AlertEvent>();
        svc.Events.Subscribe(e => received.Add(e));

        subject.OnNext(MetricsWithRam(90.0)); // 90% RAM usage > 80 threshold

        received.Should().HaveCount(1);
        received[0].Rule.Metric.Should().Be(AlertMetric.RamPercent);
        svc.Dispose();
    }

    [Fact]
    public void OnTick_GpuMetric_UsesFirstGpuUsage()
    {
        var settings = new AppSettings
        {
            AlertRules = new List<AlertRule> { MakeRule(metric: AlertMetric.GpuPercent, threshold: 80, sustainSec: 0, cooldownSec: 0) }
        };
        var (svc, subject, _) = CreateService(settings);
        svc.Start();

        var received = new List<AlertEvent>();
        svc.Events.Subscribe(e => received.Add(e));

        subject.OnNext(MetricsWithGpu(95.0)); // first GPU at 95% > 80 threshold

        received.Should().HaveCount(1);
        received[0].Rule.Metric.Should().Be(AlertMetric.GpuPercent);
        received[0].Value.Should().Be(95.0);
        svc.Dispose();
    }

    [Fact]
    public void OnTick_CpuTempMetric_UsesCpuTemperature()
    {
        var settings = new AppSettings
        {
            AlertRules = new List<AlertRule> { MakeRule(metric: AlertMetric.CpuTemperature, threshold: 80, sustainSec: 0, cooldownSec: 0) }
        };
        var (svc, subject, _) = CreateService(settings);
        svc.Start();

        var received = new List<AlertEvent>();
        svc.Events.Subscribe(e => received.Add(e));

        subject.OnNext(MetricsWithCpuTemp(90.0)); // 90°C > 80 threshold

        received.Should().HaveCount(1);
        received[0].Rule.Metric.Should().Be(AlertMetric.CpuTemperature);
        received[0].Value.Should().Be(90.0);
        svc.Dispose();
    }

    // ── Notifications ──────────────────────────────────────────────────────

    [Fact]
    public void OnTick_DesktopNotificationsEnabled_CallsShowAlert()
    {
        var settings = new AppSettings
        {
            AlertRules                  = new List<AlertRule> { MakeRule(threshold: 80, sustainSec: 0, cooldownSec: 0) },
            DesktopNotificationsEnabled = true
        };
        var (svc, subject, notifMock) = CreateService(settings);
        svc.Start();

        subject.OnNext(MetricsWithCpu(95.0));

        notifMock.Verify(
            n => n.ShowAlert(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AlertSeverity>()),
            Times.Once);
        svc.Dispose();
    }

    [Fact]
    public void OnTick_DesktopNotificationsDisabled_DoesNotCallShowAlert()
    {
        var settings = new AppSettings
        {
            AlertRules                  = new List<AlertRule> { MakeRule(threshold: 80, sustainSec: 0, cooldownSec: 0) },
            DesktopNotificationsEnabled = false
        };
        var (svc, subject, notifMock) = CreateService(settings);
        svc.Start();

        subject.OnNext(MetricsWithCpu(95.0));

        notifMock.Verify(
            n => n.ShowAlert(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AlertSeverity>()),
            Times.Never);
        svc.Dispose();
    }
}
