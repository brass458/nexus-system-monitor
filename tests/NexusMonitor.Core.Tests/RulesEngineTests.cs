using System.Reactive.Subjects;
using FluentAssertions;
using Moq;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;
using NexusMonitor.Core.Rules;
using NexusMonitor.Core.Storage;
using NexusMonitor.Core.Tests.Helpers;
using TestHelpers = NexusMonitor.Core.Tests.Helpers.MockFactory;
using Xunit;

namespace NexusMonitor.Core.Tests;

/// <summary>
/// Tests for <see cref="ProcessRule"/> pattern matching (pure logic, no mocks)
/// and <see cref="RulesEngine"/> behavioural tests using a Subject-driven stream.
/// </summary>
public class ProcessRuleMatchesTests
{
    // ── Helper ────────────────────────────────────────────────────────────────

    private static ProcessRule Rule(string pattern) =>
        new() { ProcessNamePattern = pattern };

    // ── 1. Exact match ────────────────────────────────────────────────────────

    [Fact]
    public void Matches_ExactName_ReturnsTrue()
    {
        Rule("chrome").Matches("chrome").Should().BeTrue();
    }

    // ── 2. .exe suffix in pattern stripped ────────────────────────────────────

    [Fact]
    public void Matches_PatternHasExeSuffix_StillMatchesProcessWithoutExe()
    {
        Rule("chrome.exe").Matches("chrome").Should().BeTrue();
    }

    // ── 3. .exe suffix in input stripped ─────────────────────────────────────

    [Fact]
    public void Matches_InputHasExeSuffix_StillMatchesPattern()
    {
        Rule("chrome").Matches("chrome.exe").Should().BeTrue();
    }

    // ── 4. Case-insensitive ───────────────────────────────────────────────────

    [Fact]
    public void Matches_DifferentCase_ReturnsTrue()
    {
        Rule("CHROME").Matches("chrome").Should().BeTrue();
        Rule("chrome").Matches("CHROME").Should().BeTrue();
    }

    // ── 5. Wildcard prefix ────────────────────────────────────────────────────

    [Fact]
    public void Matches_WildcardPrefix_MatchesSuffix()
    {
        Rule("*ome").Matches("chrome").Should().BeTrue();
    }

    // ── 6. Wildcard suffix ────────────────────────────────────────────────────

    [Fact]
    public void Matches_WildcardSuffix_MatchesPrefix()
    {
        Rule("chr*").Matches("chrome").Should().BeTrue();
    }

    // ── 7. Wildcard middle ────────────────────────────────────────────────────

    [Fact]
    public void Matches_WildcardMiddle_MatchesAroundMiddle()
    {
        Rule("ch*me").Matches("chrome").Should().BeTrue();
    }

    // ── 8. Wildcard all ───────────────────────────────────────────────────────

    [Fact]
    public void Matches_WildcardOnly_MatchesAnything()
    {
        Rule("*").Matches("chrome").Should().BeTrue();
        Rule("*").Matches("anything_at_all").Should().BeTrue();
    }

    // ── 9. No match ───────────────────────────────────────────────────────────

    [Fact]
    public void Matches_DifferentName_ReturnsFalse()
    {
        Rule("firefox").Matches("chrome").Should().BeFalse();
    }

    // ── 10. Empty pattern ─────────────────────────────────────────────────────

    [Fact]
    public void Matches_EmptyPattern_ReturnsFalse()
    {
        Rule("").Matches("chrome").Should().BeFalse();
        Rule("   ").Matches("chrome").Should().BeFalse();
    }
}

/// <summary>
/// Tests for <see cref="ProcessRule.Summary"/> (pure display logic, no mocks).
/// </summary>
public class ProcessRuleSummaryTests
{
    // ── 28. Summary no actions ────────────────────────────────────────────────

    [Fact]
    public void Summary_NoActionsSet_ReturnsNoActionsString()
    {
        var rule = new ProcessRule { ProcessNamePattern = "chrome" };
        rule.Summary.Should().Be("(no actions)");
    }

    // ── 29. Summary with actions ──────────────────────────────────────────────

    [Fact]
    public void Summary_WithPriority_ContainsPriorityText()
    {
        var rule = new ProcessRule
        {
            ProcessNamePattern = "chrome",
            Priority = ProcessPriority.High
        };
        rule.Summary.Should().Contain("Priority=High");
    }

    [Fact]
    public void Summary_WithDisallowed_ContainsBlock()
    {
        var rule = new ProcessRule
        {
            ProcessNamePattern = "malware",
            Disallowed = true
        };
        rule.Summary.Should().Contain("Block");
    }

    [Fact]
    public void Summary_WithMultipleActions_ContainsAllParts()
    {
        var rule = new ProcessRule
        {
            ProcessNamePattern = "chrome",
            Priority = ProcessPriority.BelowNormal,
            EfficiencyMode = true,
            MaxInstances = 3
        };
        rule.Summary.Should().Contain("Priority=BelowNormal")
                    .And.Contain("Efficiency")
                    .And.Contain("MaxInstances=3");
    }

    [Fact]
    public void Summary_WithWatchdogAction_ContainsWatchdogText()
    {
        var rule = new ProcessRule
        {
            ProcessNamePattern = "chrome",
            WatchdogAction = WatchdogAction.Terminate,
            Condition = new RuleCondition { Type = ConditionType.CpuAbove }
        };
        rule.Summary.Should().Contain("Watchdog=Terminate");
    }
}

/// <summary>
/// Behavioural tests for <see cref="RulesEngine"/>:
/// startup, persistent actions, disallowed rules, disabled rules,
/// watchdog conditions, instance limits, and preference fallback.
/// </summary>
public class RulesEngineTests : IDisposable
{
    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly Mock<IProcessProvider> _mockProvider;
    private readonly AppSettings _settings;

    public RulesEngineTests()
    {
        _mockProvider = TestHelpers.CreateProcessProvider();
        _settings = new AppSettings();
    }

    public void Dispose() { }

    // ── Factory helpers ───────────────────────────────────────────────────────

    private RulesEngine CreateEngine(ProcessRule? rule = null,
                                     IProcessProvider? provider = null)
    {
        if (rule is not null)
            _settings.Rules = new List<ProcessRule> { rule };

        return new RulesEngine(
            provider ?? _mockProvider.Object,
            _settings,
            TestHelpers.CreateLogger<RulesEngine>().Object);
    }

    private RulesEngine CreateEngineWithStore(ProcessRule? rule,
                                               NexusMonitor.Core.Storage.ProcessPreferenceStore store)
    {
        if (rule is not null)
            _settings.Rules = new List<ProcessRule> { rule };

        return new RulesEngine(
            _mockProvider.Object,
            _settings,
            TestHelpers.CreateLogger<RulesEngine>().Object,
            store);
    }

    private static ProcessInfo MakeProcess(int pid, string name,
                                            double cpu = 0, long ram = 0,
                                            DateTime startTime = default) =>
        new()
        {
            Pid = pid,
            Name = name,
            CpuPercent = cpu,
            WorkingSetBytes = ram,
            StartTime = startTime == default ? DateTime.UtcNow : startTime
        };

    /// <summary>
    /// Emits a tick on the subject and waits long enough for the async void
    /// OnTick to fully execute (SemaphoreSlim ensures serialised execution).
    /// </summary>
    private static async Task EmitAndWait(
        Subject<IReadOnlyList<ProcessInfo>> subject,
        IReadOnlyList<ProcessInfo> processes,
        int waitMs = 300)
    {
        subject.OnNext(processes);
        await Task.Delay(waitMs);
    }

    // ── Start / Stop ──────────────────────────────────────────────────────────

    // ── 11. Start() subscribes to process stream ──────────────────────────────

    [Fact]
    public void Start_SubscribesToProcessStream()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        using var engine = CreateEngine();
        engine.Start();

        _mockProvider.Verify(p => p.GetProcessStream(It.IsAny<TimeSpan>()), Times.Once);
    }

    // ── 12. Start() twice is idempotent ──────────────────────────────────────

    [Fact]
    public void Start_CalledTwice_DoesNotSubscribeTwice()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        using var engine = CreateEngine();
        engine.Start();
        engine.Start(); // second call should be a no-op

        _mockProvider.Verify(p => p.GetProcessStream(It.IsAny<TimeSpan>()), Times.Once);
    }

    // ── 13. Stop() after Start() disposes subscription ───────────────────────

    [Fact]
    public async Task Stop_AfterStart_StopsProcessingTicks()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        // Set up a rule so ticks would normally trigger actions
        var rule = new ProcessRule
        {
            ProcessNamePattern = "chrome",
            Priority = ProcessPriority.High
        };

        using var engine = CreateEngine(rule);
        engine.Start();
        engine.Stop();

        // Emit after stop — SetPriorityAsync should NOT be called
        await EmitAndWait(subject, new[] { MakeProcess(1, "chrome") });

        _mockProvider.Verify(
            p => p.SetPriorityAsync(It.IsAny<int>(), It.IsAny<ProcessPriority>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Persistent actions on new process ─────────────────────────────────────

    // ── 14. Priority rule applied on first tick ───────────────────────────────

    [Fact]
    public async Task OnTick_NewProcess_PriorityRuleApplied()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        var rule = new ProcessRule
        {
            ProcessNamePattern = "chrome",
            Priority = ProcessPriority.High,
            IsEnabled = true
        };

        using var engine = CreateEngine(rule);
        engine.Start();

        await EmitAndWait(subject, new[] { MakeProcess(1, "chrome") });

        _mockProvider.Verify(
            p => p.SetPriorityAsync(1, ProcessPriority.High, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── 15. AffinityMask rule applied on first tick ───────────────────────────

    [Fact]
    public async Task OnTick_NewProcess_AffinityRuleApplied()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        var rule = new ProcessRule
        {
            ProcessNamePattern = "chrome",
            AffinityMask = 0b1111L,
            IsEnabled = true
        };

        using var engine = CreateEngine(rule);
        engine.Start();

        await EmitAndWait(subject, new[] { MakeProcess(1, "chrome") });

        _mockProvider.Verify(
            p => p.SetAffinityAsync(1, 0b1111L, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── 16. Persistent action NOT re-applied on second tick ───────────────────

    [Fact]
    public async Task OnTick_SameProcessSecondTick_PersistentActionNotReApplied()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        var rule = new ProcessRule
        {
            ProcessNamePattern = "chrome",
            Priority = ProcessPriority.High,
            IsEnabled = true
        };

        using var engine = CreateEngine(rule);
        engine.Start();

        var procs = new[] { MakeProcess(1, "chrome") };
        await EmitAndWait(subject, procs);
        await EmitAndWait(subject, procs); // same PID — isNew = false

        // Must be called exactly once (first tick only)
        _mockProvider.Verify(
            p => p.SetPriorityAsync(1, ProcessPriority.High, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Disallowed rule ───────────────────────────────────────────────────────

    // ── 17. Disallowed process killed on first tick ───────────────────────────

    [Fact]
    public async Task OnTick_DisallowedNewProcess_KillCalled()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        var rule = new ProcessRule
        {
            ProcessNamePattern = "malware",
            Disallowed = true,
            IsEnabled = true
        };

        using var engine = CreateEngine(rule);
        engine.Start();

        await EmitAndWait(subject, new[] { MakeProcess(99, "malware") });

        _mockProvider.Verify(
            p => p.KillProcessAsync(99, It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── 18. Disallowed process on second tick is NOT killed again ─────────────

    [Fact]
    public async Task OnTick_DisallowedProcessSecondTick_KillNotCalledAgain()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        var rule = new ProcessRule
        {
            ProcessNamePattern = "malware",
            Disallowed = true,
            IsEnabled = true
        };

        using var engine = CreateEngine(rule);
        engine.Start();

        var procs = new[] { MakeProcess(99, "malware") };
        await EmitAndWait(subject, procs);
        await EmitAndWait(subject, procs); // second tick, same PID — isNew=false → no kill

        _mockProvider.Verify(
            p => p.KillProcessAsync(99, It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Disabled rule ─────────────────────────────────────────────────────────

    // ── 19. Disabled rule is not applied ─────────────────────────────────────

    [Fact]
    public async Task OnTick_DisabledRule_NoActionsApplied()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        var rule = new ProcessRule
        {
            ProcessNamePattern = "chrome",
            Priority = ProcessPriority.High,
            IsEnabled = false  // disabled
        };

        using var engine = CreateEngine(rule);
        engine.Start();

        await EmitAndWait(subject, new[] { MakeProcess(1, "chrome") });

        _mockProvider.Verify(
            p => p.SetPriorityAsync(It.IsAny<int>(), It.IsAny<ProcessPriority>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Watchdog conditions ───────────────────────────────────────────────────

    // ── 20. Watchdog fires SetIdle after CpuAbove sustained ──────────────────

    [Fact]
    public async Task Watchdog_CpuAboveWithZeroDuration_FiresOnSecondTick()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        var rule = new ProcessRule
        {
            ProcessNamePattern = "chrome",
            IsEnabled = true,
            WatchdogAction = WatchdogAction.SetIdle,
            Condition = new RuleCondition
            {
                Type = ConditionType.CpuAbove,
                CpuThresholdPercent = 50.0,
                DurationSeconds = 0   // fire immediately on second tick
            }
        };

        using var engine = CreateEngine(rule);
        engine.Start();

        var procs = new[] { MakeProcess(1, "chrome", cpu: 90.0) };

        // First tick: condition first seen — just recorded, no action yet
        await EmitAndWait(subject, procs, waitMs: 150);

        // Small real delay so (now - first) >= 0 seconds
        await Task.Delay(50);

        // Second tick: duration elapsed (0 s threshold) — action should fire
        await EmitAndWait(subject, procs, waitMs: 300);

        _mockProvider.Verify(
            p => p.SetPriorityAsync(1, ProcessPriority.Idle, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── 21. Watchdog fires Terminate after RamAbove sustained ────────────────

    [Fact]
    public async Task Watchdog_RamAboveWithZeroDuration_FiresTerminateOnSecondTick()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        const long threshold = 512L * 1024 * 1024; // 512 MB

        var rule = new ProcessRule
        {
            ProcessNamePattern = "memory-hog",
            IsEnabled = true,
            WatchdogAction = WatchdogAction.Terminate,
            Condition = new RuleCondition
            {
                Type = ConditionType.RamAbove,
                RamThresholdBytes = threshold,
                DurationSeconds = 0
            }
        };

        using var engine = CreateEngine(rule);
        engine.Start();

        var procs = new[] { MakeProcess(2, "memory-hog", ram: threshold + 1) };

        await EmitAndWait(subject, procs, waitMs: 150);
        await Task.Delay(50);
        await EmitAndWait(subject, procs, waitMs: 300);

        _mockProvider.Verify(
            p => p.KillProcessAsync(2, It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── 22. Watchdog does NOT fire before DurationSeconds elapsed ─────────────

    [Fact]
    public async Task Watchdog_ConditionNotSustainedLongEnough_DoesNotFire()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        var rule = new ProcessRule
        {
            ProcessNamePattern = "chrome",
            IsEnabled = true,
            WatchdogAction = WatchdogAction.SetIdle,
            Condition = new RuleCondition
            {
                Type = ConditionType.CpuAbove,
                CpuThresholdPercent = 50.0,
                DurationSeconds = 3600  // 1 hour — will never elapse in test
            }
        };

        using var engine = CreateEngine(rule);
        engine.Start();

        var procs = new[] { MakeProcess(1, "chrome", cpu: 90.0) };

        // Two ticks — DurationSeconds is huge, so action must NOT fire
        await EmitAndWait(subject, procs, waitMs: 150);
        await EmitAndWait(subject, procs, waitMs: 150);

        _mockProvider.Verify(
            p => p.SetPriorityAsync(1, ProcessPriority.Idle, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── 23. Watchdog resets when condition no longer met ─────────────────────

    [Fact]
    public async Task Watchdog_ConditionDropsBelowThreshold_ActionDoesNotFireAfterReset()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        var rule = new ProcessRule
        {
            ProcessNamePattern = "chrome",
            IsEnabled = true,
            WatchdogAction = WatchdogAction.SetIdle,
            Condition = new RuleCondition
            {
                Type = ConditionType.CpuAbove,
                CpuThresholdPercent = 50.0,
                DurationSeconds = 0
            }
        };

        using var engine = CreateEngine(rule);
        engine.Start();

        // First tick: condition over threshold — first-seen recorded
        await EmitAndWait(subject, new[] { MakeProcess(1, "chrome", cpu: 90.0) }, waitMs: 150);

        // Second tick: CPU drops below threshold — condition resets, no action
        await EmitAndWait(subject, new[] { MakeProcess(1, "chrome", cpu: 10.0) }, waitMs: 150);

        // Third tick: CPU over again — first-seen recorded again, no action yet
        await EmitAndWait(subject, new[] { MakeProcess(1, "chrome", cpu: 90.0) }, waitMs: 150);

        // Action should NOT have fired — the reset cleared the first-seen timestamp
        _mockProvider.Verify(
            p => p.SetPriorityAsync(1, ProcessPriority.Idle, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Watchdog: ConditionType.Always fires ──────────────────────────────────

    [Fact]
    public async Task Watchdog_AlwaysConditionWithZeroDuration_FiresOnSecondTick()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        var rule = new ProcessRule
        {
            ProcessNamePattern = "chrome",
            IsEnabled = true,
            WatchdogAction = WatchdogAction.TrimWorkingSet,
            Condition = new RuleCondition
            {
                Type = ConditionType.Always,
                DurationSeconds = 0
            }
        };

        using var engine = CreateEngine(rule);
        engine.Start();

        var procs = new[] { MakeProcess(1, "chrome") };
        await EmitAndWait(subject, procs, waitMs: 150);
        await Task.Delay(50);
        await EmitAndWait(subject, procs, waitMs: 300);

        _mockProvider.Verify(
            p => p.TrimWorkingSetAsync(1, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Instance limits ───────────────────────────────────────────────────────

    // ── 24. MaxInstances=1 with 2 matching processes — kills newest ───────────

    [Fact]
    public async Task InstanceLimit_TwoMatchingProcesses_KillsNewest()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        var rule = new ProcessRule
        {
            ProcessNamePattern = "chrome",
            MaxInstances = 1,
            IsEnabled = true
        };

        using var engine = CreateEngine(rule);
        engine.Start();

        var t0 = DateTime.UtcNow;
        var older = MakeProcess(10, "chrome", startTime: t0);
        var newer = MakeProcess(11, "chrome", startTime: t0.AddSeconds(1));

        await EmitAndWait(subject, new[] { older, newer });

        // Only the newer (PID 11) should be killed; older (PID 10) is kept
        _mockProvider.Verify(
            p => p.KillProcessAsync(11, It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockProvider.Verify(
            p => p.KillProcessAsync(10, It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── 25. MaxInstances=2 with exactly 2 processes — no kill ─────────────────

    [Fact]
    public async Task InstanceLimit_ExactlyAtLimit_NothingKilled()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        var rule = new ProcessRule
        {
            ProcessNamePattern = "chrome",
            MaxInstances = 2,
            IsEnabled = true
        };

        using var engine = CreateEngine(rule);
        engine.Start();

        var t0 = DateTime.UtcNow;
        var procs = new[]
        {
            MakeProcess(10, "chrome", startTime: t0),
            MakeProcess(11, "chrome", startTime: t0.AddSeconds(1))
        };

        await EmitAndWait(subject, procs);

        _mockProvider.Verify(
            p => p.KillProcessAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Preference fallback ───────────────────────────────────────────────────

    // ── 26. ProcessPreference applied when no rule matches ────────────────────

    [Fact]
    public async Task PreferenceFallback_NoRuleMatched_PreferenceApplied()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        using var db = new TestMetricsDatabase();
        var store = new NexusMonitor.Core.Storage.ProcessPreferenceStore(db.Database);

        // Save a preference for "notepad"
        store.Upsert(new ProcessPreference
        {
            ExeName = "notepad",
            Priority = ProcessPriority.BelowNormal
        });

        // Add a dummy rule that does NOT match "notepad" — RulesEngine returns early
        // when the enabled-rules list is empty, so we need at least one rule present
        // for the preference-fallback path to be reached.
        _settings.Rules = new List<ProcessRule>
        {
            new ProcessRule { ProcessNamePattern = "firefox", IsEnabled = true }
        };

        using var engine = CreateEngineWithStore(null, store);
        engine.Start();

        await EmitAndWait(subject, new[] { MakeProcess(5, "notepad") });

        _mockProvider.Verify(
            p => p.SetPriorityAsync(5, ProcessPriority.BelowNormal, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── 27. ProcessPreference NOT applied when rule matches ───────────────────

    [Fact]
    public async Task PreferenceFallback_RuleMatched_PreferenceNotApplied()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        using var db = new TestMetricsDatabase();
        var store = new NexusMonitor.Core.Storage.ProcessPreferenceStore(db.Database);

        // Save preference with Idle priority
        store.Upsert(new ProcessPreference
        {
            ExeName = "notepad",
            Priority = ProcessPriority.Idle
        });

        // Add an explicit rule with High priority — this wins
        var rule = new ProcessRule
        {
            ProcessNamePattern = "notepad",
            Priority = ProcessPriority.High,
            IsEnabled = true
        };

        using var engine = CreateEngineWithStore(rule, store);
        engine.Start();

        await EmitAndWait(subject, new[] { MakeProcess(5, "notepad") });

        // Rule-applied High priority — called once
        _mockProvider.Verify(
            p => p.SetPriorityAsync(5, ProcessPriority.High, It.IsAny<CancellationToken>()),
            Times.Once);

        // Preference-applied Idle priority — must NOT be called
        _mockProvider.Verify(
            p => p.SetPriorityAsync(5, ProcessPriority.Idle, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── 28. Disallowed rule sets ruleMatched — preference NOT applied ──────────

    [Fact]
    public async Task Disallowed_WithPreferenceStore_PreferenceNotApplied()
    {
        // A process that is disallowed should be killed, and its preference should NOT be applied
        // (the Disallowed branch must set ruleMatched=true before continuing)
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        using var db = new TestMetricsDatabase();
        var store = new NexusMonitor.Core.Storage.ProcessPreferenceStore(db.Database);

        // Save a preference with Idle priority for "malware"
        store.Upsert(new ProcessPreference
        {
            ExeName = "malware",
            Priority = ProcessPriority.Idle
        });

        // Disallowed rule — should kill the process and NOT fall through to preference
        var rule = new ProcessRule
        {
            ProcessNamePattern = "malware",
            Disallowed = true,
            IsEnabled = true
        };

        using var engine = CreateEngineWithStore(rule, store);
        engine.Start();

        await EmitAndWait(subject, new[] { MakeProcess(99, "malware") });

        // Kill was called
        _mockProvider.Verify(
            p => p.KillProcessAsync(99, It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Preference-applied Idle priority must NOT be called
        _mockProvider.Verify(
            p => p.SetPriorityAsync(99, ProcessPriority.Idle, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Empty rules → early return ────────────────────────────────────────────

    [Fact]
    public async Task OnTick_NoEnabledRules_NoActionsApplied()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        // Settings has no rules at all
        _settings.Rules = new List<ProcessRule>();

        using var engine = CreateEngine();
        engine.Start();

        await EmitAndWait(subject, new[] { MakeProcess(1, "chrome") });

        _mockProvider.Verify(
            p => p.SetPriorityAsync(It.IsAny<int>(), It.IsAny<ProcessPriority>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockProvider.Verify(
            p => p.KillProcessAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Unmatched process → no actions ───────────────────────────────────────

    [Fact]
    public async Task OnTick_ProcessDoesNotMatchRule_NoActionsApplied()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        var rule = new ProcessRule
        {
            ProcessNamePattern = "firefox",
            Priority = ProcessPriority.High,
            IsEnabled = true
        };

        using var engine = CreateEngine(rule);
        engine.Start();

        // Emit "chrome" — rule matches "firefox" only
        await EmitAndWait(subject, new[] { MakeProcess(1, "chrome") });

        _mockProvider.Verify(
            p => p.SetPriorityAsync(It.IsAny<int>(), It.IsAny<ProcessPriority>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Multiple persistent actions in one rule ───────────────────────────────

    [Fact]
    public async Task OnTick_NewProcess_AllPersistentActionsApplied()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        var rule = new ProcessRule
        {
            ProcessNamePattern = "chrome",
            Priority = ProcessPriority.BelowNormal,
            AffinityMask = 0x0FL,
            IoPriority = IoPriority.Low,
            EfficiencyMode = true,
            IsEnabled = true
        };

        using var engine = CreateEngine(rule);
        engine.Start();

        await EmitAndWait(subject, new[] { MakeProcess(1, "chrome") });

        _mockProvider.Verify(
            p => p.SetPriorityAsync(1, ProcessPriority.BelowNormal, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockProvider.Verify(
            p => p.SetAffinityAsync(1, 0x0FL, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockProvider.Verify(
            p => p.SetIoPriorityAsync(1, IoPriority.Low, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockProvider.Verify(
            p => p.SetEfficiencyModeAsync(1, true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Dead PIDs evicted — new spawn treated as new again ───────────────────

    [Fact]
    public async Task OnTick_ProcessDiesAndRespawns_TreatedAsNewOnRespawn()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        var rule = new ProcessRule
        {
            ProcessNamePattern = "chrome",
            Priority = ProcessPriority.High,
            IsEnabled = true
        };

        using var engine = CreateEngine(rule);
        engine.Start();

        // Tick 1: PID 1 seen — SetPriority called once (isNew=true)
        await EmitAndWait(subject, new[] { MakeProcess(1, "chrome") });

        // Tick 2: empty list — PID 1 evicted from _seenPids
        await EmitAndWait(subject, Array.Empty<ProcessInfo>());

        // Tick 3: PID 1 appears again — treated as new process → SetPriority called again
        await EmitAndWait(subject, new[] { MakeProcess(1, "chrome") });

        _mockProvider.Verify(
            p => p.SetPriorityAsync(1, ProcessPriority.High, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}

/// <summary>
/// Group-based rule matching tests for <see cref="RulesEngine"/>.
/// Verifies that rules with <see cref="ProcessRule.GroupName"/> match processes
/// that belong to the named group, with and without a <see cref="ProcessGroupStore"/>.
/// </summary>
public class RulesEngineGroupMatchingTests : IDisposable
{
    private readonly Mock<IProcessProvider> _mockProvider;
    private readonly AppSettings _settings;

    public RulesEngineGroupMatchingTests()
    {
        _mockProvider = TestHelpers.CreateProcessProvider();
        _settings = new AppSettings();
    }

    public void Dispose() { }

    private RulesEngine CreateEngineWithGroupStore(ProcessRule rule, ProcessGroupStore groupStore)
    {
        _settings.Rules = new List<ProcessRule> { rule };
        return new RulesEngine(
            _mockProvider.Object,
            _settings,
            TestHelpers.CreateLogger<RulesEngine>().Object,
            preferenceStore: null,
            groupStore: groupStore);
    }

    private RulesEngine CreateEngineWithoutGroupStore(ProcessRule rule)
    {
        _settings.Rules = new List<ProcessRule> { rule };
        return new RulesEngine(
            _mockProvider.Object,
            _settings,
            TestHelpers.CreateLogger<RulesEngine>().Object);
    }

    private static ProcessInfo MakeProcess(int pid, string name) =>
        new() { Pid = pid, Name = name, StartTime = DateTime.UtcNow };

    private static async Task EmitAndWait(
        Subject<IReadOnlyList<ProcessInfo>> subject,
        IReadOnlyList<ProcessInfo> processes,
        int waitMs = 300)
    {
        subject.OnNext(processes);
        await Task.Delay(waitMs);
    }

    // ── 1. Rule with GroupName matches process in group ───────────────────────

    [Fact]
    public async Task Rule_WithGroupName_MatchesProcessInGroup()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        using var db = new TestMetricsDatabase();
        var groupStore = new ProcessGroupStore(db.Database);
        groupStore.Upsert(new ProcessGroup { Name = "Browsers", Patterns = ["chrome*"] });

        // Rule has GroupName only — no ProcessNamePattern
        var rule = new ProcessRule
        {
            ProcessNamePattern = "",
            GroupName = "Browsers",
            Priority = ProcessPriority.BelowNormal,
            IsEnabled = true
        };

        using var engine = CreateEngineWithGroupStore(rule, groupStore);
        engine.Start();

        await EmitAndWait(subject, new[] { MakeProcess(1, "chrome") });

        _mockProvider.Verify(
            p => p.SetPriorityAsync(1, ProcessPriority.BelowNormal, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── 2. Rule with GroupName does NOT match process outside group ───────────

    [Fact]
    public async Task Rule_WithGroupName_DoesNotMatchProcessOutsideGroup()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        using var db = new TestMetricsDatabase();
        var groupStore = new ProcessGroupStore(db.Database);
        groupStore.Upsert(new ProcessGroup { Name = "Browsers", Patterns = ["chrome*"] });

        var rule = new ProcessRule
        {
            ProcessNamePattern = "",
            GroupName = "Browsers",
            Priority = ProcessPriority.BelowNormal,
            IsEnabled = true
        };

        using var engine = CreateEngineWithGroupStore(rule, groupStore);
        engine.Start();

        // "notepad" is not in the Browsers group
        await EmitAndWait(subject, new[] { MakeProcess(2, "notepad") });

        _mockProvider.Verify(
            p => p.SetPriorityAsync(It.IsAny<int>(), It.IsAny<ProcessPriority>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── 3. Rule with both pattern and GroupName matches either ────────────────

    [Fact]
    public async Task Rule_WithBothPatternAndGroup_MatchesEither()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        using var db = new TestMetricsDatabase();
        var groupStore = new ProcessGroupStore(db.Database);
        groupStore.Upsert(new ProcessGroup { Name = "Browsers", Patterns = ["chrome*"] });

        // Rule matches "notepad" by pattern AND "chrome" by group
        var rule = new ProcessRule
        {
            ProcessNamePattern = "notepad",
            GroupName = "Browsers",
            Priority = ProcessPriority.BelowNormal,
            IsEnabled = true
        };

        using var engine = CreateEngineWithGroupStore(rule, groupStore);
        engine.Start();

        // "notepad" matches via pattern
        await EmitAndWait(subject, new[] { MakeProcess(1, "notepad") });
        // "chrome" matches via group
        await EmitAndWait(subject, new[] { MakeProcess(2, "chrome") });

        _mockProvider.Verify(
            p => p.SetPriorityAsync(1, ProcessPriority.BelowNormal, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockProvider.Verify(
            p => p.SetPriorityAsync(2, ProcessPriority.BelowNormal, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── 4. Rule with GroupName but no store — falls back to pattern only ──────

    [Fact]
    public async Task Rule_WithGroupName_NullGroupStore_FallsBackToPatternOnly()
    {
        var subject = new Subject<IReadOnlyList<ProcessInfo>>();
        _mockProvider.Setup(p => p.GetProcessStream(It.IsAny<TimeSpan>()))
                     .Returns(subject);

        // No group store passed to engine
        var rule = new ProcessRule
        {
            ProcessNamePattern = "",
            GroupName = "Browsers",
            Priority = ProcessPriority.BelowNormal,
            IsEnabled = true
        };

        using var engine = CreateEngineWithoutGroupStore(rule);
        engine.Start();

        // "chrome" would match via group, but there's no store — no action expected
        await EmitAndWait(subject, new[] { MakeProcess(1, "chrome") });

        _mockProvider.Verify(
            p => p.SetPriorityAsync(It.IsAny<int>(), It.IsAny<ProcessPriority>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
