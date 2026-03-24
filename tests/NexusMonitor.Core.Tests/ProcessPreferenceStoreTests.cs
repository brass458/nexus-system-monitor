using FluentAssertions;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;
using NexusMonitor.Core.Storage;
using NexusMonitor.Core.Tests.Helpers;
using Xunit;

namespace NexusMonitor.Core.Tests;

/// <summary>
/// Tests for <see cref="ProcessPreferenceStore"/>: construction, Get, Upsert, Delete,
/// GetAll, persistence across instances, and basic thread safety.
/// All tests use a real SQLite-backed <see cref="TestMetricsDatabase"/>.
/// </summary>
public class ProcessPreferenceStoreTests
{
    // ── Initialization / construction ─────────────────────────────────────────

    [Fact]
    public void Constructor_EmptyDb_CacheIsEmpty()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessPreferenceStore(db.Database);

        store.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void Constructor_PreloadedDb_LoadsCache()
    {
        using var db = new TestMetricsDatabase();

        // Insert a row directly so the second store must load it from DB.
        var seed = new ProcessPreferenceStore(db.Database);
        seed.Upsert(new ProcessPreference { ExeName = "preloaded", Priority = ProcessPriority.High });

        // New store instance over same DB — constructor calls LoadCache().
        var store = new ProcessPreferenceStore(db.Database);
        store.Get("preloaded").Should().NotBeNull();
        store.Get("preloaded")!.Priority.Should().Be(ProcessPriority.High);
    }

    // ── Get ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Get_ExistingPreference_ReturnsIt()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessPreferenceStore(db.Database);
        store.Upsert(new ProcessPreference { ExeName = "notepad", Priority = ProcessPriority.Normal });

        var result = store.Get("notepad");

        result.Should().NotBeNull();
        result!.ExeName.Should().Be("notepad");
        result.Priority.Should().Be(ProcessPriority.Normal);
    }

    [Fact]
    public void Get_NonExistent_ReturnsNull()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessPreferenceStore(db.Database);

        store.Get("nonexistent").Should().BeNull();
    }

    [Fact]
    public void Get_NormalizesExeExtension()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessPreferenceStore(db.Database);
        store.Upsert(new ProcessPreference { ExeName = "chrome", Priority = ProcessPriority.AboveNormal });

        // Lookup with .exe extension — should still find the entry.
        var result = store.Get("chrome.exe");

        result.Should().NotBeNull();
        result!.ExeName.Should().Be("chrome");
    }

    [Fact]
    public void Get_CaseInsensitive()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessPreferenceStore(db.Database);
        store.Upsert(new ProcessPreference { ExeName = "chrome", Priority = ProcessPriority.Normal });

        store.Get("CHROME").Should().NotBeNull();
        store.Get("Chrome").Should().NotBeNull();
    }

    // ── Upsert ────────────────────────────────────────────────────────────────

    [Fact]
    public void Upsert_NewEntry_CanBeRetrievedByGet()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessPreferenceStore(db.Database);

        store.Upsert(new ProcessPreference { ExeName = "explorer", Priority = ProcessPriority.Normal });

        store.Get("explorer").Should().NotBeNull();
    }

    [Fact]
    public void Upsert_ExistingEntry_UpdatesInPlace()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessPreferenceStore(db.Database);

        store.Upsert(new ProcessPreference { ExeName = "myapp", Priority = ProcessPriority.Normal });
        store.Upsert(new ProcessPreference { ExeName = "myapp", Priority = ProcessPriority.High });

        var result = store.Get("myapp");
        result.Should().NotBeNull();
        result!.Priority.Should().Be(ProcessPriority.High);
        store.GetAll().Should().HaveCount(1);
    }

    [Fact]
    public void Upsert_NormalizesExeName()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessPreferenceStore(db.Database);

        store.Upsert(new ProcessPreference { ExeName = "Chrome.exe", Priority = ProcessPriority.BelowNormal });

        // The stored key must be "chrome" (lowercased, no extension).
        store.Get("chrome").Should().NotBeNull();
        store.Get("chrome")!.ExeName.Should().Be("chrome");
    }

    [Fact]
    public void Upsert_AllFields_AllFieldsPersistedAndReadBack()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessPreferenceStore(db.Database);
        var before = DateTime.UtcNow.AddSeconds(-1);

        store.Upsert(new ProcessPreference
        {
            ExeName        = "fulltest",
            Priority       = ProcessPriority.High,
            AffinityMask   = 0b1111L,
            IoPriority     = IoPriority.Low,
            MemoryPriority = MemoryPriority.High,
            EfficiencyMode = true,
        });

        var result = store.Get("fulltest");
        result.Should().NotBeNull();
        result!.Priority.Should().Be(ProcessPriority.High);
        result.AffinityMask.Should().Be(0b1111L);
        result.IoPriority.Should().Be(IoPriority.Low);
        result.MemoryPriority.Should().Be(MemoryPriority.High);
        result.EfficiencyMode.Should().BeTrue();
        result.ModifiedUtc.Should().BeAfter(before);
    }

    [Fact]
    public void Upsert_NullableFields_NullValuesStoredAndReadBack()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessPreferenceStore(db.Database);

        // Only Priority set; all other nullable fields left null.
        store.Upsert(new ProcessPreference { ExeName = "sparse", Priority = ProcessPriority.Idle });

        var result = store.Get("sparse");
        result.Should().NotBeNull();
        result!.Priority.Should().Be(ProcessPriority.Idle);
        result.AffinityMask.Should().BeNull();
        result.IoPriority.Should().BeNull();
        result.MemoryPriority.Should().BeNull();
        result.EfficiencyMode.Should().BeNull();
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_ExistingEntry_RemovedFromGetAndGetAll()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessPreferenceStore(db.Database);
        store.Upsert(new ProcessPreference { ExeName = "todelete", Priority = ProcessPriority.Normal });

        store.Delete("todelete");

        store.Get("todelete").Should().BeNull();
        store.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void Delete_NonExistentEntry_NoException()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessPreferenceStore(db.Database);

        var act = () => store.Delete("doesnotexist");

        act.Should().NotThrow();
    }

    [Fact]
    public void Delete_NormalizesExeName()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessPreferenceStore(db.Database);
        store.Upsert(new ProcessPreference { ExeName = "chrome", Priority = ProcessPriority.Normal });

        // Delete using un-normalized form.
        store.Delete("CHROME.EXE");

        store.Get("chrome").Should().BeNull();
    }

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public void GetAll_MultipleEntries_ReturnsSortedByExeName()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessPreferenceStore(db.Database);
        store.Upsert(new ProcessPreference { ExeName = "zebra" });
        store.Upsert(new ProcessPreference { ExeName = "alpha" });
        store.Upsert(new ProcessPreference { ExeName = "mango" });

        var all = store.GetAll();

        all.Select(p => p.ExeName).Should().BeInAscendingOrder();
        all.Should().HaveCount(3);
    }

    [Fact]
    public void GetAll_Empty_ReturnsEmptyList()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessPreferenceStore(db.Database);

        store.GetAll().Should().BeEmpty();
    }

    // ── Persistence across instances ──────────────────────────────────────────

    [Fact]
    public void Persistence_UpsertedDataSurvivesNewInstance()
    {
        using var db = new TestMetricsDatabase();

        var store1 = new ProcessPreferenceStore(db.Database);
        store1.Upsert(new ProcessPreference
        {
            ExeName  = "persistent",
            Priority = ProcessPriority.RealTime,
        });

        // Create a completely new store object over the same underlying DB connection.
        var store2 = new ProcessPreferenceStore(db.Database);
        var result = store2.Get("persistent");

        result.Should().NotBeNull();
        result!.Priority.Should().Be(ProcessPriority.RealTime);
    }

    // ── Thread safety ─────────────────────────────────────────────────────────

    [Fact]
    public void ThreadSafety_ConcurrentUpserts_NoException()
    {
        using var db = new TestMetricsDatabase();
        var store = new ProcessPreferenceStore(db.Database);

        var act = () => Parallel.For(0, 50, i =>
        {
            store.Upsert(new ProcessPreference
            {
                ExeName  = $"proc{i:D3}",
                Priority = ProcessPriority.Normal,
            });
        });

        act.Should().NotThrow();
        store.GetAll().Should().HaveCount(50);
    }
}
