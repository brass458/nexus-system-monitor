using FluentAssertions;
using NexusMonitor.Core.Storage;
using Xunit;

namespace NexusMonitor.Core.Tests;

/// <summary>
/// Pure-math tests for <see cref="SlidingStats"/>.
/// No mocks or I/O — all assertions are on deterministic arithmetic.
/// </summary>
public class SlidingStatsTests
{
    // ── Construction / empty ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_WindowSize5_PropertyReturns5()
    {
        var stats = new SlidingStats(5);
        stats.WindowSize.Should().Be(5);
    }

    [Fact]
    public void Mean_Empty_ReturnsZero()
    {
        var stats = new SlidingStats(5);
        stats.Mean().Should().BeApproximately(0, 0.001);
    }

    // ── Push and Mean ─────────────────────────────────────────────────────────

    [Fact]
    public void Mean_SingleSample_ReturnsThatValue()
    {
        var stats = new SlidingStats(5);
        stats.Push(42);
        stats.Mean().Should().BeApproximately(42, 0.001);
    }

    [Fact]
    public void Mean_MultipleSamples_ReturnsCorrectAverage()
    {
        // push [10, 20, 30] into window=5 → mean = (10+20+30)/3 = 20
        var stats = new SlidingStats(5);
        stats.Push(10);
        stats.Push(20);
        stats.Push(30);
        stats.Mean().Should().BeApproximately(20, 0.001);
    }

    [Fact]
    public void Push_OverflowsBuffer_EvictsOldest()
    {
        // window=3, push [1,2,3,4] → oldest (1) evicted → mean = (2+3+4)/3 = 3.0
        var stats = new SlidingStats(3);
        stats.Push(1);
        stats.Push(2);
        stats.Push(3);
        stats.Push(4);
        stats.Mean().Should().BeApproximately(3.0, 0.001);
    }

    [Fact]
    public void Push_ExactlyFull_AllValuesIncluded()
    {
        // window=3, push exactly 3 values → all included
        var stats = new SlidingStats(3);
        stats.Push(5);
        stats.Push(10);
        stats.Push(15);
        stats.Mean().Should().BeApproximately(10, 0.001);
    }

    [Fact]
    public void Mean_AllSameValue_ReturnsThatValue()
    {
        var stats = new SlidingStats(4);
        foreach (var _ in Enumerable.Range(0, 4))
            stats.Push(7);
        stats.Mean().Should().BeApproximately(7, 0.001);
    }

    // ── IsAnomaly ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsAnomaly_InsufficientSamples_ReturnsFalse()
    {
        // windowSize=10, push 3 samples (< WindowSize/2 = 5) → warmup guard triggers
        var stats = new SlidingStats(10);
        stats.Push(10);
        stats.Push(10);
        stats.Push(10);
        stats.IsAnomaly(999, kSigma: 2).Should().BeFalse();
    }

    [Fact]
    public void IsAnomaly_BelowThreshold_ReturnsFalse()
    {
        // constant 50s → mean=50, stdDev=0; test value 50 → 50 > 50+2*0 is false
        var stats = new SlidingStats(6);
        foreach (var _ in Enumerable.Range(0, 6))
            stats.Push(50);
        stats.IsAnomaly(50, kSigma: 2).Should().BeFalse();
    }

    [Fact]
    public void IsAnomaly_AboveThreshold_ReturnsTrue()
    {
        // constant 10s → stdDev=0, mean=10; any value > 10 is an anomaly
        var stats = new SlidingStats(6);
        foreach (var _ in Enumerable.Range(0, 6))
            stats.Push(10);
        stats.IsAnomaly(50, kSigma: 2).Should().BeTrue();
    }

    [Fact]
    public void IsAnomaly_ExactlyAtThreshold_ReturnsFalse()
    {
        // constant 10s → stdDev=0, mean=10; value=10 → 10 > 10+2*0 is false (strict >)
        var stats = new SlidingStats(6);
        foreach (var _ in Enumerable.Range(0, 6))
            stats.Push(10);
        stats.IsAnomaly(10, kSigma: 2).Should().BeFalse();
    }

    [Fact]
    public void IsAnomaly_AfterWarmup_WarmupGuardPasses()
    {
        // window=4, WindowSize/2=2; push exactly 2 values → guard passes → result depends on math
        var stats = new SlidingStats(4);
        stats.Push(10);
        stats.Push(10);
        // count==2 == WindowSize/2 → warmup guard no longer blocks
        // mean=10, stdDev=0 → 50 > 10 → true
        stats.IsAnomaly(50, kSigma: 2).Should().BeTrue();
    }

    // ── Standard deviation ────────────────────────────────────────────────────

    [Fact]
    public void StdDev_ConstantValues_IsZero_SoAnyValueAboveMeanIsAnomaly()
    {
        // stdDev=0 when all values are equal → mean+k*0 = mean → anything above mean triggers
        var stats = new SlidingStats(5);
        foreach (var _ in Enumerable.Range(0, 5))
            stats.Push(25);
        stats.IsAnomaly(25.001, kSigma: 3).Should().BeTrue();
    }

    [Fact]
    public void IsAnomaly_WithVariance_CorrectDetection()
    {
        // push [8,9,10,11,12,10,10,10] into window=8
        // mean ≈ 10, stdDev > 0; test value 20 should be anomaly at 2σ
        var stats = new SlidingStats(8);
        foreach (var v in new[] { 8.0, 9.0, 10.0, 11.0, 12.0, 10.0, 10.0, 10.0 })
            stats.Push(v);
        stats.IsAnomaly(20, kSigma: 2).Should().BeTrue();
    }

    // ── Window size edge cases ────────────────────────────────────────────────

    [Fact]
    public void WindowSize1_SingleSample_WorksCorrectly()
    {
        var stats = new SlidingStats(1);
        stats.Push(99);
        stats.Mean().Should().BeApproximately(99, 0.001);
    }
}
