using Microsoft.Data.Sqlite;

namespace NexusMonitor.Core.Storage;

/// <summary>
/// Owns the SQLite connection and schema. One instance per application lifetime.
/// Call Dispose() on application shutdown to flush WAL and release the file lock.
/// </summary>
public sealed class MetricsDatabase : IDisposable
{
    private readonly SqliteConnection _conn;
    private bool _disposed;

    public MetricsDatabase(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        ConfigurePragmas();
        InitSchema();
    }

    public SqliteConnection Connection => _conn;

    // ── WAL + performance pragmas ──────────────────────────────────────────────
    private void ConfigurePragmas()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode  = WAL;
            PRAGMA synchronous   = NORMAL;
            PRAGMA cache_size    = -8000;
            PRAGMA temp_store    = MEMORY;
            PRAGMA busy_timeout  = 5000;
            PRAGMA foreign_keys  = OFF;";
        cmd.ExecuteNonQuery();
    }

    // ── Schema ─────────────────────────────────────────────────────────────────
    private void InitSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"

-- Schema version tracking + rollup watermarks
CREATE TABLE IF NOT EXISTS meta (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

-- 1 row per collection tick (CPU, RAM, disk, net, GPU)
CREATE TABLE IF NOT EXISTS system_metrics (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    ts                  INTEGER NOT NULL,

    cpu_percent         REAL    NOT NULL,
    cpu_freq_mhz        REAL,
    cpu_temp_c          REAL,
    process_count       INTEGER,
    thread_count        INTEGER,
    handle_count        INTEGER,

    mem_used_bytes      INTEGER NOT NULL,
    mem_total_bytes     INTEGER NOT NULL,
    mem_cached_bytes    INTEGER,
    mem_committed_bytes INTEGER,

    disk_read_bps       INTEGER,
    disk_write_bps      INTEGER,

    net_send_bps        INTEGER,
    net_recv_bps        INTEGER,

    gpu_percent         REAL,
    gpu_mem_used        INTEGER,
    gpu_mem_total       INTEGER,
    gpu_temp_c          REAL
);
CREATE INDEX IF NOT EXISTS idx_sm_ts ON system_metrics(ts);

-- Top-N resource consumers per tick
CREATE TABLE IF NOT EXISTS process_snapshots (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    ts              INTEGER NOT NULL,
    pid             INTEGER NOT NULL,
    name            TEXT    NOT NULL,
    cpu_percent     REAL,
    mem_bytes       INTEGER,
    io_read_bps     INTEGER,
    io_write_bps    INTEGER,
    gpu_percent     REAL,
    net_send_bps    INTEGER,
    net_recv_bps    INTEGER,
    category        INTEGER
);
CREATE INDEX IF NOT EXISTS idx_ps_ts   ON process_snapshots(ts);
CREATE INDEX IF NOT EXISTS idx_ps_name ON process_snapshots(name, ts);

-- Active network connections per tick
CREATE TABLE IF NOT EXISTS network_snapshots (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    ts              INTEGER NOT NULL,
    protocol        INTEGER,
    local_addr      TEXT,
    local_port      INTEGER,
    remote_addr     TEXT,
    remote_port     INTEGER,
    state           INTEGER,
    pid             INTEGER,
    process_name    TEXT,
    send_bps        INTEGER,
    recv_bps        INTEGER
);
CREATE INDEX IF NOT EXISTS idx_ns_ts     ON network_snapshots(ts);
CREATE INDEX IF NOT EXISTS idx_ns_remote ON network_snapshots(remote_addr, ts);

-- Detected anomalies and threshold breaches
CREATE TABLE IF NOT EXISTS events (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    ts              INTEGER NOT NULL,
    event_type      TEXT    NOT NULL,
    severity        INTEGER NOT NULL,
    metric_name     TEXT,
    metric_value    REAL,
    threshold       REAL,
    description     TEXT,
    metadata_json   TEXT
);
CREATE INDEX IF NOT EXISTS idx_ev_ts   ON events(ts);
CREATE INDEX IF NOT EXISTS idx_ev_type ON events(event_type, ts);

-- Classified resource incidents (EventMonitorService)
CREATE TABLE IF NOT EXISTS resource_events (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    ts                  INTEGER NOT NULL,
    end_ts              INTEGER,
    resource            INTEGER NOT NULL,
    peak_pct            REAL    NOT NULL,
    avg_pct             REAL    NOT NULL,
    duration_sec        REAL    NOT NULL,
    primary_process     TEXT,
    primary_process_pid INTEGER NOT NULL DEFAULT 0,
    classification      INTEGER NOT NULL,
    severity            INTEGER NOT NULL,
    summary             TEXT
);
CREATE INDEX IF NOT EXISTS idx_re_ts    ON resource_events(ts);
CREATE INDEX IF NOT EXISTS idx_re_class ON resource_events(classification, ts);

-- 1-minute rollups (retained 7 days)
CREATE TABLE IF NOT EXISTS rollups_1m (
    ts              INTEGER PRIMARY KEY,
    cpu_avg         REAL,  cpu_max REAL,
    mem_avg_bytes   INTEGER, mem_max_bytes INTEGER,
    disk_read_avg   INTEGER, disk_write_avg INTEGER,
    net_send_avg    INTEGER, net_recv_avg   INTEGER,
    gpu_avg         REAL,  gpu_max REAL,
    sample_count    INTEGER
);

-- 5-minute rollups (retained 30 days)
CREATE TABLE IF NOT EXISTS rollups_5m (
    ts              INTEGER PRIMARY KEY,
    cpu_avg         REAL,  cpu_max REAL,
    mem_avg_bytes   INTEGER, mem_max_bytes INTEGER,
    disk_read_avg   INTEGER, disk_write_avg INTEGER,
    net_send_avg    INTEGER, net_recv_avg   INTEGER,
    gpu_avg         REAL,  gpu_max REAL,
    sample_count    INTEGER
);

-- 1-hour rollups (retained 1 year)
CREATE TABLE IF NOT EXISTS rollups_1h (
    ts              INTEGER PRIMARY KEY,
    cpu_avg         REAL,  cpu_max REAL,
    mem_avg_bytes   INTEGER, mem_max_bytes INTEGER,
    disk_read_avg   INTEGER, disk_write_avg INTEGER,
    net_send_avg    INTEGER, net_recv_avg   INTEGER,
    gpu_avg         REAL,  gpu_max REAL,
    sample_count    INTEGER
);

INSERT OR IGNORE INTO meta (key, value) VALUES
    ('schema_version',    '1'),
    ('last_rollup_1m_ts', '0'),
    ('last_rollup_5m_ts', '0'),
    ('last_rollup_1h_ts', '0');
";
        cmd.ExecuteNonQuery();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────
    public long GetMeta(string key)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        var result = cmd.ExecuteScalar();
        return result is string s && long.TryParse(s, out var v) ? v : 0L;
    }

    public void SetMeta(string key, long value)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO meta (key, value) VALUES ($k, $v)";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value.ToString());
        cmd.ExecuteNonQuery();
    }

    public long GetDatabaseSizeBytes()
    {
        try
        {
            var source = _conn.DataSource;
            return string.IsNullOrEmpty(source) || !File.Exists(source)
                ? 0L
                : new FileInfo(source).Length;
        }
        catch { return 0L; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            // Checkpoint WAL into main db file before closing
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }
        catch { }
        _conn.Dispose();
    }
}
