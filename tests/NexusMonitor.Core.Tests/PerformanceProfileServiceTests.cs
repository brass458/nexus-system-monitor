using System.Reactive.Subjects;
using FluentAssertions;
using Moq;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Automation;
using NexusMonitor.Core.Gaming;
using NexusMonitor.Core.Models;
using NexusMonitor.Core.Tests.Helpers;
using Xunit;

namespace NexusMonitor.Core.Tests;

public class PerformanceProfileServiceTests
{
    // ── Helpers ────────────────────────────────────────────────────────────

    private static ProcessInfo MakeProcess(string name, int pid = 1234)
        => new ProcessInfo { Pid = pid, Name = name };

    private (PerformanceProfileService svc,
             Subject<IReadOnlyList<ProcessInfo>> processSubject,
             Mock<IProcessProvider> mockProcess,
             Mock<IPowerPlanProvider> mockPower)
        CreateService(AppSettings? settings = null)
    {
        var mockProcess = Helpers.MockFactory.CreateProcessProvider();
        var mockPower   = new Mock<IPowerPlanProvider>(MockBehavior.Loose);
        var logger      = Helpers.MockFactory.CreateLogger<PerformanceProfileService>();
        settings      ??= new AppSettings();

        var processSubject = new Subject<IReadOnlyList<ProcessInfo>>();
        mockProcess.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
            .Returns(processSubject);

        var svc = new PerformanceProfileService(
            mockProcess.Object, mockPower.Object, settings, logger.Object);
        return (svc, processSubject, mockProcess, mockPower);
    }

    private (AppSettings settings, PerformanceProfile profile, Guid profileId) MakeSettings(
        string processName   = "chrome",
        ProcessPriority? priority = ProcessPriority.High,
        bool? efficiencyMode = null,
        bool changePowerPlan = false,
        string powerPlanName = "")
    {
        var profileId = Guid.NewGuid();
        var profile = new PerformanceProfile
        {
            Id              = profileId,
            Name            = "TestProfile",
            ChangePowerPlan = changePowerPlan,
            PowerPlanName   = powerPlanName,
            ProcessRules    = new List<ProfileProcessRule>
            {
                new() { ProcessNamePattern = processName, Priority = priority, EfficiencyMode = efficiencyMode }
            }
        };
        var settings = new AppSettings { PerformanceProfiles = new List<PerformanceProfile> { profile } };
        return (settings, profile, profileId);
    }

    // ── Basic activate / deactivate ────────────────────────────────────────

    [Fact]
    public void ActivateProfile_ValidId_SetsIsActive()
    {
        var (settings, _, profileId) = MakeSettings();
        var (svc, _, _, _) = CreateService(settings);

        svc.ActivateProfile(profileId);

        svc.IsActive.Should().BeTrue();
        svc.Dispose();
    }

    [Fact]
    public void ActivateProfile_ValidId_SetsActiveProfileId()
    {
        var (settings, _, profileId) = MakeSettings();
        var (svc, _, _, _) = CreateService(settings);

        svc.ActivateProfile(profileId);

        svc.ActiveProfileId.Should().Be(profileId);
        svc.Dispose();
    }

    [Fact]
    public void ActivateProfile_ValidId_SetsActiveProfileName()
    {
        var (settings, profile, profileId) = MakeSettings();
        var (svc, _, _, _) = CreateService(settings);

        svc.ActivateProfile(profileId);

        svc.ActiveProfileName.Should().Be(profile.Name);
        svc.Dispose();
    }

    [Fact]
    public void ActivateProfile_InvalidId_NoChange()
    {
        var (settings, _, _) = MakeSettings();
        var (svc, _, _, _) = CreateService(settings);

        svc.ActivateProfile(Guid.NewGuid()); // ID not in settings

        svc.IsActive.Should().BeFalse();
        svc.Dispose();
    }

    [Fact]
    public void ActivateProfile_WhenAlreadyActive_DeactivatesFirst()
    {
        // Set up two profiles in settings
        var profileAId = Guid.NewGuid();
        var profileBId = Guid.NewGuid();
        var profileA = new PerformanceProfile { Id = profileAId, Name = "ProfileA" };
        var profileB = new PerformanceProfile { Id = profileBId, Name = "ProfileB" };
        var settings = new AppSettings
        {
            PerformanceProfiles = new List<PerformanceProfile> { profileA, profileB }
        };
        var (svc, _, _, _) = CreateService(settings);

        svc.ActivateProfile(profileAId);
        svc.ActivateProfile(profileBId);

        svc.ActiveProfileId.Should().Be(profileBId);
        svc.ActiveProfileName.Should().Be("ProfileB");
        svc.Dispose();
    }

    [Fact]
    public void DeactivateProfile_WhenActive_SetsIsActiveFalse()
    {
        var (settings, _, profileId) = MakeSettings();
        var (svc, _, _, _) = CreateService(settings);

        svc.ActivateProfile(profileId);
        svc.DeactivateProfile();

        svc.IsActive.Should().BeFalse();
        svc.Dispose();
    }

    [Fact]
    public void DeactivateProfile_WhenNotActive_NoOp()
    {
        var (svc, _, _, _) = CreateService();

        var act = () => svc.DeactivateProfile();

        act.Should().NotThrow();
        svc.Dispose();
    }

    // ── Observables ────────────────────────────────────────────────────────

    [Fact]
    public void ActivateProfile_FiresProfileChanged()
    {
        var (settings, profile, profileId) = MakeSettings();
        var (svc, _, _, _) = CreateService(settings);

        string? received = null;
        svc.ProfileChanged.Subscribe(name => received = name);

        svc.ActivateProfile(profileId);

        received.Should().Be(profile.Name);
        svc.Dispose();
    }

    [Fact]
    public void DeactivateProfile_FiresProfileChangedNull()
    {
        var (settings, _, profileId) = MakeSettings();
        var (svc, _, _, _) = CreateService(settings);

        var received = new List<string?>();
        svc.ProfileChanged.Subscribe(name => received.Add(name));

        svc.ActivateProfile(profileId);
        svc.DeactivateProfile();

        received.Should().HaveCount(2);
        received[0].Should().NotBeNull();  // profile name on activate
        received[1].Should().BeNull();     // null on deactivate
        svc.Dispose();
    }

    [Fact]
    public void ActivateProfile_FiresStatusMessage()
    {
        var (settings, profile, profileId) = MakeSettings();
        var (svc, _, _, _) = CreateService(settings);

        var messages = new List<string>();
        svc.StatusMessages.Subscribe(m => messages.Add(m));

        svc.ActivateProfile(profileId);

        messages.Should().Contain(m => m.Contains("activated") && m.Contains(profile.Name));
        svc.Dispose();
    }

    [Fact]
    public void DeactivateProfile_FiresStatusMessage()
    {
        var (settings, profile, profileId) = MakeSettings();
        var (svc, _, _, _) = CreateService(settings);

        var messages = new List<string>();
        svc.StatusMessages.Subscribe(m => messages.Add(m));

        svc.ActivateProfile(profileId);
        svc.DeactivateProfile();

        messages.Should().Contain(m => m.Contains("deactivated") && m.Contains(profile.Name));
        svc.Dispose();
    }

    // ── Process priority application ───────────────────────────────────────

    [Fact]
    public async Task ActivateProfile_SetsProcessPriority()
    {
        var (settings, _, profileId) = MakeSettings(
            processName: "chrome",
            priority: ProcessPriority.High);
        var (svc, processSubject, mockProcess, _) = CreateService(settings);

        svc.ActivateProfile(profileId);

        // Emit processes — satisfies the FirstAsync in ApplyProfileAsync
        processSubject.OnNext(new[] { MakeProcess("chrome", 1000) });
        await Task.Delay(200);

        mockProcess.Verify(p => p.SetPriorityAsync(1000, ProcessPriority.High, It.IsAny<CancellationToken>()),
            Times.Once);
        svc.Dispose();
    }

    [Fact]
    public async Task DeactivateProfile_RestoresProcessToNormal()
    {
        var (settings, _, profileId) = MakeSettings(
            processName: "chrome",
            priority: ProcessPriority.High);
        var (svc, processSubject, mockProcess, _) = CreateService(settings);

        svc.ActivateProfile(profileId);
        processSubject.OnNext(new[] { MakeProcess("chrome", 1000) });
        await Task.Delay(200); // let ApplyProfileAsync complete and add to _boostedPids

        svc.DeactivateProfile();
        await Task.Delay(200); // let RestoreProcessesAsync complete

        mockProcess.Verify(p => p.SetPriorityAsync(1000, ProcessPriority.Normal, It.IsAny<CancellationToken>()),
            Times.Once);
        svc.Dispose();
    }

    // ── Power plan ─────────────────────────────────────────────────────────

    [Fact]
    public void ActivateProfile_WithPowerPlan_CallsSetActivePlan()
    {
        var originalPlan = Guid.NewGuid();
        var highPerfGuid = Guid.NewGuid();

        var (settings, _, profileId) = MakeSettings(
            changePowerPlan: true,
            powerPlanName: "High Performance");
        var (svc, _, _, mockPower) = CreateService(settings);

        mockPower.Setup(p => p.GetActivePlan()).Returns(originalPlan);
        mockPower.Setup(p => p.GetPowerPlans()).Returns(new List<PowerPlanInfo>
        {
            new PowerPlanInfo(highPerfGuid, "High Performance", false)
        });

        svc.ActivateProfile(profileId);

        mockPower.Verify(p => p.SetActivePlan(highPerfGuid), Times.Once);
        svc.Dispose();
    }

    [Fact]
    public void DeactivateProfile_RestoresPowerPlan()
    {
        var originalPlan = Guid.NewGuid();
        var highPerfGuid = Guid.NewGuid();

        var (settings, _, profileId) = MakeSettings(
            changePowerPlan: true,
            powerPlanName: "High Performance");
        var (svc, _, _, mockPower) = CreateService(settings);

        mockPower.Setup(p => p.GetActivePlan()).Returns(originalPlan);
        mockPower.Setup(p => p.GetPowerPlans()).Returns(new List<PowerPlanInfo>
        {
            new PowerPlanInfo(highPerfGuid, "High Performance", false)
        });

        svc.ActivateProfile(profileId);
        svc.DeactivateProfile();

        // Should restore to originalPlan
        mockPower.Verify(p => p.SetActivePlan(originalPlan), Times.Once);
        svc.Dispose();
    }

    // ── Idempotency / safety ───────────────────────────────────────────────

    [Fact]
    public async Task ActivateProfile_SamePidTwice_OnlyAppliesOnce()
    {
        var (settings, _, profileId) = MakeSettings(
            processName: "chrome",
            priority: ProcessPriority.High);
        var (svc, processSubject, mockProcess, _) = CreateService(settings);

        svc.ActivateProfile(profileId);

        // Emit the same process twice — _boostedPids.Add returns false on second call
        processSubject.OnNext(new[] { MakeProcess("chrome", 1000) });
        await Task.Delay(200);

        // Emit again — applyLock is released, new apply triggered via polling or second emit
        // But _boostedPids already contains 1000, so SetPriorityAsync should NOT be called again
        processSubject.OnNext(new[] { MakeProcess("chrome", 1000) });
        await Task.Delay(200);

        mockProcess.Verify(p => p.SetPriorityAsync(1000, ProcessPriority.High, It.IsAny<CancellationToken>()),
            Times.Once);
        svc.Dispose();
    }

    [Fact]
    public void Dispose_CallsDeactivate()
    {
        var (settings, _, profileId) = MakeSettings();
        var (svc, _, _, _) = CreateService(settings);

        string? lastProfileChanged = "sentinel"; // non-null sentinel
        svc.ProfileChanged.Subscribe(name => lastProfileChanged = name);

        svc.ActivateProfile(profileId);
        // At this point lastProfileChanged == profile.Name
        svc.Dispose();

        // Dispose calls DeactivateProfile which fires ProfileChanged(null)
        lastProfileChanged.Should().BeNull();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var (settings, _, profileId) = MakeSettings();
        var (svc, _, _, _) = CreateService(settings);
        svc.ActivateProfile(profileId);

        var act = () =>
        {
            svc.Dispose();
            svc.Dispose();
        };

        act.Should().NotThrow();
    }
}
