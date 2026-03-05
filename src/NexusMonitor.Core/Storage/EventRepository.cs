using Microsoft.Data.Sqlite;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Storage;

/// <summary>Write interface for classified resource incidents.</summary>
public interface IResourceEventWriter
{
    Task InsertResourceEventAsync(ResourceEvent evt);
}

/// <summary>Read interface for classified resource incidents.</summary>
public interface IResourceEventReader
{
    Task<IReadOnlyList<ResourceEvent>> GetResourceEventsAsync(
        DateTimeOffset           from,
        DateTimeOffset           to,
        EventClassification?     classification = null,
        CancellationToken        ct             = default);
}

/// <summary>
/// SQLite-backed repository for the <c>resource_events</c> table.
/// Uses a separate read-only connection (WAL mode) alongside the shared write connection.
/// </summary>
public sealed class EventRepository : IResourceEventWriter, IResourceEventReader, IDisposable
{
    private readonly MetricsDatabase  _db;
    private readonly SqliteConnection _readConn;
    private bool _disposed;

    public EventRepository(MetricsDatabase db)
    {
        _db = db;

        _readConn = new SqliteConnection($"Data Source={_db.Connection.DataSource}");
        _readConn.Open();
        using var pragma = _readConn.CreateCommand();
        pragma.CommandText = "PRAGMA query_only = ON; PRAGMA journal_mode = WAL;";
        pragma.ExecuteNonQuery();
    }

    // ── Write ──────────────────────────────────────────────────────────────────

    public Task InsertResourceEventAsync(ResourceEvent evt) =>
        Task.Run(() =>
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO resource_events
                    (ts, end_ts, resource, peak_pct, avg_pct, duration_sec,
                     primary_process, primary_process_pid, classification, severity, summary)
                VALUES
                    ($ts, $endTs, $res, $peak, $avg, $dur,
                     $proc, $pid, $class, $sev, $summary)";

            cmd.Parameters.AddWithValue("$ts",
                new DateTimeOffset(evt.Timestamp, TimeSpan.Zero).ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$endTs",
                evt.EndTimestamp.HasValue
                    ? (object)new DateTimeOffset(evt.EndTimestamp.Value, TimeSpan.Zero).ToUnixTimeMilliseconds()
                    : DBNull.Value);
            cmd.Parameters.AddWithValue("$res",     (int)evt.Resource);
            cmd.Parameters.AddWithValue("$peak",    evt.PeakUsagePercent);
            cmd.Parameters.AddWithValue("$avg",     evt.AverageUsagePercent);
            cmd.Parameters.AddWithValue("$dur",     evt.Duration.TotalSeconds);
            cmd.Parameters.AddWithValue("$proc",
                string.IsNullOrEmpty(evt.PrimaryProcess) ? (object)DBNull.Value : evt.PrimaryProcess);
            cmd.Parameters.AddWithValue("$pid",     evt.PrimaryProcessPid);
            cmd.Parameters.AddWithValue("$class",   (int)evt.Classification);
            cmd.Parameters.AddWithValue("$sev",     evt.Severity);
            cmd.Parameters.AddWithValue("$summary",
                string.IsNullOrEmpty(evt.Summary) ? (object)DBNull.Value : evt.Summary);

            cmd.ExecuteNonQuery();
        });

    // ── Read ───────────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<ResourceEvent>> GetResourceEventsAsync(
        DateTimeOffset       from,
        DateTimeOffset       to,
        EventClassification? classification = null,
        CancellationToken    ct             = default) =>
        Task.Run(() =>
        {
            var sql = @"
                SELECT id, ts, end_ts, resource, peak_pct, avg_pct, duration_sec,
                       primary_process, primary_process_pid, classification, severity, summary
                FROM   resource_events
                WHERE  ts BETWEEN $from AND $to";

            if (classification.HasValue)
                sql += " AND classification = $class";

            sql += " ORDER BY ts DESC LIMIT 500";

            using var cmd = _readConn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$to",   to.ToUnixTimeMilliseconds());
            if (classification.HasValue)
                cmd.Parameters.AddWithValue("$class", (int)classification.Value);

            var results = new List<ResourceEvent>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();

                var endTsMs = reader.IsDBNull(2) ? (long?)null : reader.GetInt64(2);

                results.Add(new ResourceEvent
                {
                    Id                  = reader.GetInt64(0),
                    Timestamp           = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)).UtcDateTime,
                    EndTimestamp        = endTsMs.HasValue
                        ? DateTimeOffset.FromUnixTimeMilliseconds(endTsMs.Value).UtcDateTime
                        : null,
                    Resource            = (ResourceType)reader.GetInt32(3),
                    PeakUsagePercent    = reader.GetDouble(4),
                    AverageUsagePercent = reader.GetDouble(5),
                    Duration            = TimeSpan.FromSeconds(reader.GetDouble(6)),
                    PrimaryProcess      = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                    PrimaryProcessPid   = reader.GetInt32(8),
                    Classification      = (EventClassification)reader.GetInt32(9),
                    Severity            = reader.GetInt32(10),
                    Summary             = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                });
            }

            return (IReadOnlyList<ResourceEvent>)results;
        }, ct);

    // ── IDisposable ────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _readConn.Dispose();
    }
}
