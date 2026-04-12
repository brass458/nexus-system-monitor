using FluentAssertions;
using NexusMonitor.Core.Models;
using NexusMonitor.Core.Storage;
using NexusMonitor.Core.Tests.Helpers;
using Xunit;

namespace NexusMonitor.Core.Tests;

/// <summary>
/// Tests for <see cref="ProcessGroupStore"/>: construction, Get, Upsert, Delete,
/// GetAll, FindGroupForProcess, and persistence across instances.
/// All tests use a real SQLite-backed <see cref="TestMetricsDatabase"/>.
/// </summary>
public class ProcessGroupStoreTests
{
    // ── Initialization / construction ─────────────────────────────────────────

    [Fact]
    public void Constructor_EmptyDb_CacheIsEmpty()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessGroupStore(db.Database);

        store.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void Constructor_PreloadedDb_LoadsCache()
    {
        using var db = new TestMetricsDatabase();

        // Insert a row via the first store instance.
        var seed = new ProcessGroupStore(db.Database);
        var group = new ProcessGroup { Name = "PreloadedGroup", Color = "#FF0000" };
        seed.Upsert(group);

        // New store instance over same DB — constructor calls LoadCache().
        var store = new ProcessGroupStore(db.Database);
        store.Get(group.Id).Should().NotBeNull();
        store.Get(group.Id)!.Name.Should().Be("PreloadedGroup");
    }

    // ── Get ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Get_ExistingGroup_ReturnsIt()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessGroupStore(db.Database);
        var group = new ProcessGroup { Name = "Browsers", Color = "#AABBCC" };
        store.Upsert(group);

        var result = store.Get(group.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(group.Id);
        result.Name.Should().Be("Browsers");
        result.Color.Should().Be("#AABBCC");
    }

    [Fact]
    public void Get_NonExistent_ReturnsNull()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessGroupStore(db.Database);

        store.Get(Guid.NewGuid()).Should().BeNull();
    }

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public void GetAll_MultipleGroups_ReturnsSortedByName()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessGroupStore(db.Database);
        store.Upsert(new ProcessGroup { Name = "Zebra" });
        store.Upsert(new ProcessGroup { Name = "Alpha" });
        store.Upsert(new ProcessGroup { Name = "Mango" });

        var all = store.GetAll();

        all.Select(g => g.Name).Should().BeInAscendingOrder();
        all.Should().HaveCount(3);
    }

    // ── Upsert ────────────────────────────────────────────────────────────────

    [Fact]
    public void Upsert_NewEntry_CanBeRetrievedByGet()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessGroupStore(db.Database);
        var group = new ProcessGroup { Name = "Games", Patterns = ["steam", "epic*"] };

        store.Upsert(group);

        store.Get(group.Id).Should().NotBeNull();
    }

    [Fact]
    public void Upsert_ExistingEntry_UpdatesInPlace()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessGroupStore(db.Database);
        var group = new ProcessGroup { Name = "OldName", Color = "#111111", Patterns = ["old*"] };
        store.Upsert(group);

        group.Name     = "NewName";
        group.Color    = "#222222";
        group.Patterns = ["new*", "updated*"];
        store.Upsert(group);

        var result = store.Get(group.Id);
        result.Should().NotBeNull();
        result!.Name.Should().Be("NewName");
        result.Color.Should().Be("#222222");
        result.Patterns.Should().BeEquivalentTo(["new*", "updated*"]);
        store.GetAll().Should().HaveCount(1);
    }

    [Fact]
    public void Upsert_Update_AdvancesModifiedUtc_PreservesCreatedUtc()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessGroupStore(db.Database);
        var group = new ProcessGroup { Name = "TimingTest" };
        store.Upsert(group);

        var after1 = store.Get(group.Id)!;
        var createdSnapshot  = after1.CreatedUtc;
        var modifiedSnapshot = after1.ModifiedUtc;

        Thread.Sleep(10); // ensure clock advances
        group.Name = "Updated";
        store.Upsert(group);

        var after2 = store.Get(group.Id)!;
        after2.CreatedUtc.Should().Be(createdSnapshot);
        after2.ModifiedUtc.Should().BeAfter(modifiedSnapshot);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_ExistingEntry_Removed()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessGroupStore(db.Database);
        var group = new ProcessGroup { Name = "ToDelete" };
        store.Upsert(group);

        store.Delete(group.Id);

        store.Get(group.Id).Should().BeNull();
        store.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void Delete_NonExistent_NoException()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessGroupStore(db.Database);

        var act = () => store.Delete(Guid.NewGuid());

        act.Should().NotThrow();
    }

    // ── FindGroupForProcess ───────────────────────────────────────────────────

    [Fact]
    public void FindGroupForProcess_MatchesPattern_ReturnsGroup()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessGroupStore(db.Database);
        var group = new ProcessGroup { Name = "Browsers", Patterns = ["chrome", "firefox", "msedge"] };
        store.Upsert(group);

        var result = store.FindGroupForProcess("chrome");

        result.Should().NotBeNull();
        result!.Id.Should().Be(group.Id);
    }

    [Fact]
    public void FindGroupForProcess_NoMatch_ReturnsNull()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessGroupStore(db.Database);
        var group = new ProcessGroup { Name = "Browsers", Patterns = ["chrome", "firefox"] };
        store.Upsert(group);

        var result = store.FindGroupForProcess("notepad");

        result.Should().BeNull();
    }

    [Fact]
    public void FindGroupForProcess_MultipleGroups_ReturnsFirstInserted()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessGroupStore(db.Database);

        // Two groups that both match "chrome" — first inserted (earlier CreatedUtc) wins.
        var first  = new ProcessGroup { Name = "First",  Patterns = ["chrome"], CreatedUtc = DateTime.UtcNow.AddMinutes(-10) };
        var second = new ProcessGroup { Name = "Second", Patterns = ["chrome"], CreatedUtc = DateTime.UtcNow };
        store.Upsert(first);
        store.Upsert(second);

        var result = store.FindGroupForProcess("chrome");

        result.Should().NotBeNull();
        result!.Name.Should().Be("First");
    }

    // ── Persistence across instances ──────────────────────────────────────────

    [Fact]
    public void Persistence_DataSurvivesNewInstance()
    {
        using var db = new TestMetricsDatabase();

        var store1 = new ProcessGroupStore(db.Database);
        var group = new ProcessGroup
        {
            Name     = "Persistent",
            Color    = "#CAFEBA",
            Patterns = ["persist*"],
        };
        store1.Upsert(group);

        // New store object over the same underlying DB connection — must load from DB.
        var store2 = new ProcessGroupStore(db.Database);
        var result = store2.Get(group.Id);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Persistent");
        result.Color.Should().Be("#CAFEBA");
        result.Patterns.Should().BeEquivalentTo(["persist*"]);
    }
}
