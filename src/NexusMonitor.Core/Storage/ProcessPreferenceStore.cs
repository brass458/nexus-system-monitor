using Microsoft.Data.Sqlite;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Storage;

/// <summary>
/// Persistent store for per-exe process preferences.
/// Backed by SQLite (process_preferences table in MetricsDatabase).
/// Maintains an in-memory cache for fast per-tick lookups from RulesEngine.
/// All public methods are thread-safe.
/// </summary>
public sealed class ProcessPreferenceStore
{
    private readonly SqliteConnection _conn;
    private readonly object _lock = new();
    private Dictionary<string, ProcessPreference> _cache = new();

    public ProcessPreferenceStore(MetricsDatabase db)
    {
        _conn = db.Connection;
        LoadCache();
    }

    // ── Cache ──────────────────────────────────────────────────────────────────

    private void LoadCache()
    {
        var result = new Dictionary<string, ProcessPreference>();
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT exe_name, priority, affinity_mask, io_priority, memory_priority, efficiency_mode, created_utc, modified_utc FROM process_preferences";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var pref = ReadRow(reader);
                result[pref.ExeName] = pref;
            }
            _cache = result;
        }
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Returns the preference for the given exe name, or null if not set.</summary>
    public ProcessPreference? Get(string exeName)
    {
        var key = ProcessPreference.NormalizeExeName(exeName);
        lock (_lock) return _cache.TryGetValue(key, out var p) ? p : null;
    }

    /// <summary>Returns all saved preferences.</summary>
    public IReadOnlyList<ProcessPreference> GetAll()
    {
        lock (_lock) return _cache.Values.OrderBy(p => p.ExeName).ToList();
    }

    /// <summary>Saves or updates a preference and refreshes the cache.</summary>
    public void Upsert(ProcessPreference pref)
    {
        pref.ExeName     = ProcessPreference.NormalizeExeName(pref.ExeName);
        pref.ModifiedUtc = DateTime.UtcNow;

        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO process_preferences
                    (exe_name, priority, affinity_mask, io_priority, memory_priority, efficiency_mode, created_utc, modified_utc)
                VALUES
                    ($exe, $pri, $aff, $io, $mem, $eff, $created, $modified)";
            cmd.Parameters.AddWithValue("$exe",     pref.ExeName);
            cmd.Parameters.AddWithValue("$pri",     pref.Priority.HasValue     ? (object)(int)pref.Priority.Value     : DBNull.Value);
            cmd.Parameters.AddWithValue("$aff",     pref.AffinityMask.HasValue ? (object)pref.AffinityMask.Value      : DBNull.Value);
            cmd.Parameters.AddWithValue("$io",      pref.IoPriority.HasValue   ? (object)(int)pref.IoPriority.Value   : DBNull.Value);
            cmd.Parameters.AddWithValue("$mem",     pref.MemoryPriority.HasValue? (object)(int)pref.MemoryPriority.Value: DBNull.Value);
            cmd.Parameters.AddWithValue("$eff",     pref.EfficiencyMode.HasValue? (object)(pref.EfficiencyMode.Value ? 1 : 0): DBNull.Value);
            cmd.Parameters.AddWithValue("$created", pref.CreatedUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$modified",pref.ModifiedUtc.ToString("O"));
            cmd.ExecuteNonQuery();

            _cache[pref.ExeName] = pref;
        }
    }

    /// <summary>Deletes a preference and refreshes the cache.</summary>
    public void Delete(string exeName)
    {
        var key = ProcessPreference.NormalizeExeName(exeName);
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM process_preferences WHERE exe_name = $exe";
            cmd.Parameters.AddWithValue("$exe", key);
            cmd.ExecuteNonQuery();
            _cache.Remove(key);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static ProcessPreference ReadRow(SqliteDataReader r)
    {
        static T? NullableEnum<T>(SqliteDataReader row, int col) where T : struct, Enum =>
            row.IsDBNull(col) ? null : (T)(object)row.GetInt32(col);

        return new ProcessPreference
        {
            ExeName       = r.GetString(0),
            Priority      = NullableEnum<ProcessPriority>(r, 1),
            AffinityMask  = r.IsDBNull(2) ? null : r.GetInt64(2),
            IoPriority    = NullableEnum<IoPriority>(r, 3),
            MemoryPriority= NullableEnum<MemoryPriority>(r, 4),
            EfficiencyMode= r.IsDBNull(5) ? null : r.GetInt32(5) != 0,
            CreatedUtc    = r.IsDBNull(6) ? DateTime.UtcNow : DateTime.Parse(r.GetString(6)),
            ModifiedUtc   = r.IsDBNull(7) ? DateTime.UtcNow : DateTime.Parse(r.GetString(7)),
        };
    }
}
