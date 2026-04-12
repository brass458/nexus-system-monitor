using System.Text.Json;
using Microsoft.Data.Sqlite;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Storage;

/// <summary>
/// Persistent store for process groups.
/// Backed by SQLite (process_groups table in MetricsDatabase).
/// Maintains an in-memory cache for fast lookups.
/// All public methods are thread-safe.
/// </summary>
public sealed class ProcessGroupStore
{
    private readonly SqliteConnection _conn;
    private readonly object _lock = new();
    private Dictionary<Guid, ProcessGroup> _cache = new();
    private List<ProcessGroup> _sortedGroups = [];

    public ProcessGroupStore(MetricsDatabase db)
    {
        _conn = db.Connection;
        LoadCache();
    }

    // ── Cache ──────────────────────────────────────────────────────────────────

    private void RebuildSortedGroups()
    {
        // Called inside _lock — caller holds _lock
        _sortedGroups = _cache.Values.OrderBy(g => g.CreatedUtc).ToList();
    }

    private void LoadCache()
    {
        var result = new Dictionary<Guid, ProcessGroup>();
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT id, name, color, patterns_json, created_utc, modified_utc FROM process_groups";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var group = ReadRow(reader);
                result[group.Id] = group;
            }
            _cache = result;
            RebuildSortedGroups();
        }
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Returns the group with the given id, or null if not found.</summary>
    public ProcessGroup? Get(Guid id)
    {
        lock (_lock) return _cache.TryGetValue(id, out var g) ? g : null;
    }

    /// <summary>Returns all groups sorted by Name.</summary>
    public IReadOnlyList<ProcessGroup> GetAll()
    {
        lock (_lock) return _cache.Values.OrderBy(g => g.Name).ToList();
    }

    /// <summary>Saves or updates a group and refreshes the cache. Mutates <paramref name="group"/>.ModifiedUtc.</summary>
    public void Upsert(ProcessGroup group)
    {
        group.ModifiedUtc = DateTime.UtcNow;

        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO process_groups
                    (id, name, color, patterns_json, created_utc, modified_utc)
                VALUES
                    ($id, $name, $color, $patterns, $created, $modified)";
            cmd.Parameters.AddWithValue("$id",       group.Id.ToString());
            cmd.Parameters.AddWithValue("$name",     group.Name);
            cmd.Parameters.AddWithValue("$color",    group.Color);
            cmd.Parameters.AddWithValue("$patterns", JsonSerializer.Serialize(group.Patterns));
            cmd.Parameters.AddWithValue("$created",  group.CreatedUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$modified", group.ModifiedUtc.ToString("O"));
            cmd.ExecuteNonQuery();

            _cache[group.Id] = group;
            RebuildSortedGroups();
        }
    }

    /// <summary>Deletes a group and refreshes the cache.</summary>
    public void Delete(Guid id)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM process_groups WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id.ToString());
            cmd.ExecuteNonQuery();
            _cache.Remove(id);
            RebuildSortedGroups();
        }
    }

    /// <summary>
    /// Returns the first group (by ascending CreatedUtc) whose patterns match processName,
    /// or null if no group matches.
    /// </summary>
    public ProcessGroup? FindGroupForProcess(string processName)
    {
        lock (_lock)
            return _sortedGroups.FirstOrDefault(g => g.Matches(processName));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static ProcessGroup ReadRow(SqliteDataReader r)
    {
        return new ProcessGroup
        {
            Id          = Guid.Parse(r.GetString(0)),
            Name        = r.GetString(1),
            Color       = r.GetString(2),
            Patterns    = JsonSerializer.Deserialize<List<string>>(r.GetString(3)) ?? [],
            CreatedUtc  = r.IsDBNull(4) ? DateTime.UtcNow : DateTime.Parse(r.GetString(4)),
            ModifiedUtc = r.IsDBNull(5) ? DateTime.UtcNow : DateTime.Parse(r.GetString(5)),
        };
    }
}
