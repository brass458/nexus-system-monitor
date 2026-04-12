using FluentAssertions;
using NexusMonitor.Core.Models;
using Xunit;

namespace NexusMonitor.Core.Tests;

/// <summary>
/// Tests for <see cref="ProcessGroup.Matches"/>: exact, case-insensitive, wildcard,
/// multi-pattern, and negative cases.
/// </summary>
public class ProcessGroupTests
{
    // ── Exact name matching ───────────────────────────────────────────────────

    [Fact]
    public void Matches_ExactName_ReturnsTrue()
    {
        var group = new ProcessGroup { Patterns = ["chrome"] };

        group.Matches("chrome").Should().BeTrue();
    }

    [Fact]
    public void Matches_ExactName_CaseInsensitive()
    {
        var group = new ProcessGroup { Patterns = ["chrome"] };

        group.Matches("Chrome").Should().BeTrue();
        group.Matches("CHROME").Should().BeTrue();
    }

    // ── .exe extension stripping ──────────────────────────────────────────────

    [Fact]
    public void Matches_ExeExtensionStripped_ReturnsTrue()
    {
        var group = new ProcessGroup { Patterns = ["chrome"] };

        group.Matches("chrome.exe").Should().BeTrue();
    }

    // ── Wildcard matching ─────────────────────────────────────────────────────

    [Fact]
    public void Matches_WildcardSuffix_ReturnsTrue()
    {
        var group = new ProcessGroup { Patterns = ["chrome*"] };

        group.Matches("chrome_helper").Should().BeTrue();
    }

    [Fact]
    public void Matches_WildcardPrefix_ReturnsTrue()
    {
        var group = new ProcessGroup { Patterns = ["*helper"] };

        group.Matches("notepadhelper").Should().BeTrue();
    }

    // ── Multiple patterns ─────────────────────────────────────────────────────

    [Fact]
    public void Matches_MultiplePatterns_MatchesAny()
    {
        var group = new ProcessGroup { Patterns = ["chrome*", "firefox*"] };

        group.Matches("firefox").Should().BeTrue();
    }

    // ── Negative cases ────────────────────────────────────────────────────────

    [Fact]
    public void Matches_NoPatterns_ReturnsFalse()
    {
        var group = new ProcessGroup { Patterns = [] };

        group.Matches("chrome").Should().BeFalse();
    }

    [Fact]
    public void Matches_NoneMatch_ReturnsFalse()
    {
        var group = new ProcessGroup { Patterns = ["chrome*", "firefox*"] };

        group.Matches("notepad").Should().BeFalse();
    }
}
