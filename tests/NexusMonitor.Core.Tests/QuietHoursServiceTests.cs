using FluentAssertions;
using NexusMonitor.Core.Automation;
using NexusMonitor.Core.Models;
using Xunit;

namespace NexusMonitor.Core.Tests;

/// <summary>
/// Tests for QuietHoursService time-window evaluation.
/// Tests use a Func&lt;DateTime&gt; clock injection so time is deterministic.
/// </summary>
public class QuietHoursServiceTests
{
    private static AppSettings MakeSettings(
        string start = "22:00", string end = "07:00",
        List<DayOfWeek>? days = null, bool enabled = true)
    {
        return new AppSettings
        {
            QuietHoursEnabled = enabled,
            QuietHoursStart   = start,
            QuietHoursEnd     = end,
            QuietHoursDays    = days ?? new List<DayOfWeek>(),
        };
    }

    [Fact]
    public void IsActive_Disabled_AlwaysFalse()
    {
        var settings = MakeSettings(enabled: false);
        var svc = new QuietHoursService(settings, () => new DateTime(2025, 1, 6, 23, 0, 0));
        svc.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_OvernightWindow_DuringWindow_ReturnsTrue()
    {
        var settings = MakeSettings("22:00", "07:00");
        var svc = new QuietHoursService(settings, () => new DateTime(2025, 1, 6, 23, 30, 0));
        svc.IsActive.Should().BeTrue();
    }

    [Fact]
    public void IsActive_OvernightWindow_AfterMidnight_ReturnsTrue()
    {
        var settings = MakeSettings("22:00", "07:00");
        var svc = new QuietHoursService(settings, () => new DateTime(2025, 1, 7, 2, 0, 0));
        svc.IsActive.Should().BeTrue();
    }

    [Fact]
    public void IsActive_OvernightWindow_AfterEndTime_ReturnsFalse()
    {
        var settings = MakeSettings("22:00", "07:00");
        var svc = new QuietHoursService(settings, () => new DateTime(2025, 1, 6, 8, 0, 0));
        svc.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_SameDayWindow_DuringWindow_ReturnsTrue()
    {
        var settings = MakeSettings("12:00", "14:00");
        var svc = new QuietHoursService(settings, () => new DateTime(2025, 1, 6, 13, 0, 0));
        svc.IsActive.Should().BeTrue();
    }

    [Fact]
    public void IsActive_SameDayWindow_OutsideWindow_ReturnsFalse()
    {
        var settings = MakeSettings("12:00", "14:00");
        var svc = new QuietHoursService(settings, () => new DateTime(2025, 1, 6, 15, 0, 0));
        svc.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_DayFilter_CorrectDay_ReturnsTrue()
    {
        var settings = MakeSettings("22:00", "07:00", days: new List<DayOfWeek> { DayOfWeek.Monday });
        var svc = new QuietHoursService(settings, () => new DateTime(2025, 1, 6, 23, 0, 0));
        svc.IsActive.Should().BeTrue();
    }

    [Fact]
    public void IsActive_DayFilter_WrongDay_ReturnsFalse()
    {
        var settings = MakeSettings("22:00", "07:00", days: new List<DayOfWeek> { DayOfWeek.Monday });
        var svc = new QuietHoursService(settings, () => new DateTime(2025, 1, 7, 23, 0, 0));
        svc.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_EmptyDayFilter_MatchesAllDays()
    {
        var settings = MakeSettings("22:00", "07:00", days: new List<DayOfWeek>());
        var svc = new QuietHoursService(settings, () => new DateTime(2025, 1, 11, 23, 0, 0));
        svc.IsActive.Should().BeTrue();
    }

    [Fact]
    public void IsActive_OvernightWindow_DayFilterAppliesToStartDay()
    {
        var settings = MakeSettings("22:00", "07:00", days: new List<DayOfWeek> { DayOfWeek.Monday });
        var svc = new QuietHoursService(settings, () => new DateTime(2025, 1, 7, 2, 0, 0)); // Tuesday 02:00
        svc.IsActive.Should().BeTrue();
    }

    [Fact]
    public void IsActive_ExactStartTime_ReturnsTrue()
    {
        var settings = MakeSettings("10:00", "12:00");
        var svc = new QuietHoursService(settings, () => new DateTime(2025, 1, 6, 10, 0, 0));
        svc.IsActive.Should().BeTrue();
    }

    [Fact]
    public void IsActive_ExactEndTime_ReturnsFalse()
    {
        var settings = MakeSettings("10:00", "12:00");
        var svc = new QuietHoursService(settings, () => new DateTime(2025, 1, 6, 12, 0, 0));
        svc.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_InvalidTimeFormat_ReturnsFalse()
    {
        var settings = MakeSettings("not-a-time", "also-bad");
        var svc = new QuietHoursService(settings, () => new DateTime(2025, 1, 6, 23, 0, 0));
        svc.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_SingleDigitHour_ParsesCorrectly()
    {
        var settings = MakeSettings("9:00", "12:00");
        var svc = new QuietHoursService(settings, () => new DateTime(2025, 1, 6, 10, 0, 0));
        svc.IsActive.Should().BeTrue();
    }

    [Fact]
    public void EvaluateCurrent_FiresIsActiveChanged_WhenStateFlips()
    {
        var settings = MakeSettings("22:00", "07:00");
        var now = new DateTime(2025, 1, 6, 21, 55, 0);
        var svc = new QuietHoursService(settings, () => now);
        svc.IsActive.Should().BeFalse();

        var events = new List<bool>();
        svc.IsActiveChanged.Subscribe(v => events.Add(v));

        now = new DateTime(2025, 1, 6, 22, 5, 0);
        svc.EvaluateCurrent();

        events.Should().ContainSingle().Which.Should().BeTrue();
        svc.IsActive.Should().BeTrue();
    }
}
