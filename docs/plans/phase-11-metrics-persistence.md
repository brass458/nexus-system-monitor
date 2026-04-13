> [!warning] DEPRECATED — Reference Only
> This plan was executed in early March 2026 (Phase 11, pre-v0.1.5). The `MetricsStore` service described here has been fully implemented and has been through multiple rounds of bug fixes since. Do not use this as a guide for current work. It exists as historical context for the SQLite storage architecture decisions.

# Phase 11 — Metrics Persistence Engine

**Goal:** Store time-series system metrics locally so they can be viewed historically, queried for events, and exported to external observability tools (Telegraf, Grafana) in future phases.

**Architecture:** A new `MetricsStore` service in Core that taps into the existing `ISystemMetricsProvider` and `IProcessProvider` Rx streams, persists snapshots to a local SQLite database, and manages retention/rollup automatically. No UI changes in this phase — the store is a backend foundation.

**New dependency:** `Microsoft.Data.Sqlite` (3.x) — added to `NexusMonitor.Core.csproj`

**Storage location:** `%AppData%/NexusMonitor/metrics.db` (alongside existing `settings.json`)

---

## Architecture Overview

```
ISystemMetricsProvider.GetMetricsStream(1s)
    ↓
MetricsStore (subscribes)
    ↓ writes
SQLite: metrics.db
    ├── system_metrics      (1 row per tick — CPU, RAM, disk, net, GPU)
    ├── process_snapshots   (top-N consumers per tick)
    ├── network_snapshots   (connection summary per tick)
    ├── events              (detected anomalies and threshold breaches)
    ├── rollups_1m          (1-minute aggregates)
    ├── rollups_5m          (5-minute aggregates)
    └── rollups_1h          (1-hour aggregates)
```

---

## Task 1: Add Microsoft.Data.Sqlite to Core

**Files:**
- Modify: `src/NexusMonitor.Core/NexusMonitor.Core.csproj`

Add package reference:
```xml
<PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.1" />
```

Why SQLite:
- Cross-platform (Windows, macOS, Linux) — no external process
- Single-file database — easy to backup, relocate, delete
- Excellent write performance for append-heavy time-series workloads
- Queryable by external tools (DB Browser for SQLite, Python, etc.)
- Well-supported in .NET via Microsoft.Data.Sqlite

**Commit:** `chore: add Microsoft.Data.Sqlite dependency to Core`

---

## Task 2: Database schema and initialization

**Files:**
- Create: `src/NexusMonitor.Core/Storage/MetricsDatabase.cs`

This class owns the SQLite connection and schema initialization.

```csharp
namespace NexusMonitor.Core.Storage;

public sealed class MetricsDatabase : IDisposable
{
    private readonly SqliteConnection _conn;

    public MetricsDatabase(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        InitSchema();
        ConfigurePragmas();
    }

    public SqliteConnection Connection => _conn;
}
```

### Schema

**`system_metrics`** — 1 row per collection tick (every UpdateIntervalMs)

```sql
CREATE TABLE IF NOT EXISTS system_metrics (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    ts              INTEGER NOT NULL,  -- Unix epoch milliseconds (UTC)

    -- CPU
    cpu_percent     REAL NOT NULL,
    cpu_freq_mhz    REAL,
    cpu_temp_c      REAL,
    process_count   INTEGER,
    thread_count    INTEGER,
    handle_count    INTEGER,

    -- Memory
    mem_used_bytes  INTEGER NOT NULL,
    mem_total_bytes INTEGER NOT NULL,
    mem_cached_bytes INTEGER,
    mem_committed_bytes INTEGER,

    -- Disk (aggregated across all drives)
    disk_read_bps   INTEGER,
    disk_write_bps  INTEGER,

    -- Network (aggregated across all adapters)
    net_send_bps    INTEGER,
    net_recv_bps    INTEGER,

    -- GPU (primary GPU)
    gpu_percent     REAL,
    gpu_mem_used    INTEGER,
    gpu_mem_total   INTEGER,
    gpu_temp_c      REAL
);

CREATE INDEX IF NOT EXISTS idx_system_metrics_ts ON system_metrics(ts);
```

**`process_snapshots`** — top-N resource consumers per tick

```sql
CREATE TABLE IF NOT EXISTS process_snapshots (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    ts              INTEGER NOT NULL,
    pid             INTEGER NOT NULL,
    name            TEXT NOT NULL,
    cpu_percent     REAL,
    mem_bytes       INTEGER,
    io_read_bps     INTEGER,
    io_write_bps    INTEGER,
    gpu_percent     REAL,
    net_send_bps    INTEGER,
    net_recv_bps    INTEGER,
    category        INTEGER  -- ProcessCategory enum
);

CREATE INDEX IF NOT EXISTS idx_process_snapshots_ts ON process_snapshots(ts);
CREATE INDEX IF NOT EXISTS idx_process_snapshots_name ON process_snapshots(name, ts);
```

**`network_snapshots`** — connection summary per tick

```sql
CREATE TABLE IF NOT EXISTS network_snapshots (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    ts              INTEGER NOT NULL,
    protocol        INTEGER,  -- ConnectionProtocol enum
    local_addr      TEXT,
    local_port      INTEGER,
    remote_addr     TEXT,
    remote_port     INTEGER,
    state           INTEGER,  -- TcpConnectionState enum
    pid             INTEGER,
    process_name    TEXT,
    send_bps        INTEGER,
    recv_bps        INTEGER
);

CREATE INDEX IF NOT EXISTS idx_network_snapshots_ts ON network_snapshots(ts);
CREATE INDEX IF NOT EXISTS idx_network_snapshots_remote ON network_snapshots(remote_addr, ts);
```

**`events`** — detected anomalies and threshold breaches

```sql
CREATE TABLE IF NOT EXISTS events (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    ts              INTEGER NOT NULL,
    event_type      TEXT NOT NULL,     -- 'cpu_high', 'mem_high', 'gpu_high', 'net_anomaly', 'new_connection', etc.
    severity        INTEGER NOT NULL,  -- 0=info, 1=warning, 2=critical
    metric_name     TEXT,
    metric_value    REAL,
    threshold       REAL,
    description     TEXT,
    metadata_json   TEXT               -- flexible JSON blob for event-specific data
);

CREATE INDEX IF NOT EXISTS idx_events_ts ON events(ts);
CREATE INDEX IF NOT EXISTS idx_events_type ON events(event_type, ts);
```

**Rollup tables** — aggregated data for longer retention

```sql
CREATE TABLE IF NOT EXISTS rollups_1m (
    ts              INTEGER PRIMARY KEY,  -- start of 1-minute bucket
    cpu_avg         REAL, cpu_max REAL,
    mem_avg_bytes   INTEGER, mem_max_bytes INTEGER,
    disk_read_avg   INTEGER, disk_write_avg INTEGER,
    net_send_avg    INTEGER, net_recv_avg   INTEGER,
    gpu_avg         REAL, gpu_max REAL,
    sample_count    INTEGER
);

CREATE TABLE IF NOT EXISTS rollups_5m (
    ts              INTEGER PRIMARY KEY,
    cpu_avg         REAL, cpu_max REAL,
    mem_avg_bytes   INTEGER, mem_max_bytes INTEGER,
    disk_read_avg   INTEGER, disk_write_avg INTEGER,
    net_send_avg    INTEGER, net_recv_avg   INTEGER,
    gpu_avg         REAL, gpu_max REAL,
    sample_count    INTEGER
);

CREATE TABLE IF NOT EXISTS rollups_1h (
    ts              INTEGER PRIMARY KEY,
    cpu_avg         REAL, cpu_max REAL,
    mem_avg_bytes   INTEGER, mem_max_bytes INTEGER,
    disk_read_avg   INTEGER, disk_write_avg INTEGER,
    net_send_avg    INTEGER, net_recv_avg   INTEGER,
    gpu_avg         REAL, gpu_max REAL,
    sample_count    INTEGER
);
```

### SQLite Pragmas (performance tuning)

```sql
PRAGMA journal_mode = WAL;       -- concurrent reads during writes
PRAGMA synchronous = NORMAL;     -- safe + fast (fsync on checkpoint only)
PRAGMA cache_size = -8000;       -- 8 MB page cache
PRAGMA temp_store = MEMORY;
PRAGMA busy_timeout = 5000;
```

WAL mode is critical — it allows the UI to read historical data while the writer appends new rows without blocking.

**Commit:** `feat(phase-11): database schema and initialization`

---

## Task 3: MetricsStore — write pipeline

**Files:**
- Create: `src/NexusMonitor.Core/Storage/MetricsStore.cs`
- Create: `src/NexusMonitor.Core/Storage/MetricsStoreConfig.cs`

### MetricsStoreConfig

```csharp
public class MetricsStoreConfig
{
    public int  TopNProcesses          { get; set; } = 15;    // top consumers per tick
    public int  NetworkSnapshotMaxRows { get; set; } = 200;   // max connections per tick
    public bool RecordNetworkSnapshots { get; set; } = true;
    public int  WriteBufferSize        { get; set; } = 30;    // batch insert every N ticks

    // Retention
    public TimeSpan RawRetention     { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan Rollup1mRetention { get; set; } = TimeSpan.FromDays(7);
    public TimeSpan Rollup5mRetention { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan Rollup1hRetention { get; set; } = TimeSpan.FromDays(365);
}
```

### MetricsStore

```csharp
public sealed class MetricsStore : IDisposable
{
    private readonly MetricsDatabase   _db;
    private readonly MetricsStoreConfig _config;
    private readonly ISystemMetricsProvider _metricsProvider;
    private readonly IProcessProvider _processProvider;
    private readonly INetworkConnectionsProvider _networkProvider;

    private IDisposable? _metricsSubscription;
    private IDisposable? _processSubscription;
    private IDisposable? _networkSubscription;

    // Write buffers (batch inserts for performance)
    private readonly List<SystemMetrics> _metricsBuffer = new();
    private readonly List<(long ts, IReadOnlyList<ProcessInfo> procs)> _processBuffer = new();
    private readonly List<(long ts, IReadOnlyList<NetworkConnection> conns)> _networkBuffer = new();
    private readonly object _lock = new();

    public void Start(TimeSpan interval) { /* subscribe to streams */ }
    public void Stop() { /* flush + dispose subscriptions */ }

    private void OnMetricsTick(SystemMetrics m) { /* buffer + flush if full */ }
    private void OnProcessTick(IReadOnlyList<ProcessInfo> procs) { /* buffer top-N */ }
    private void OnNetworkTick(IReadOnlyList<NetworkConnection> conns) { /* buffer snapshot */ }

    private void Flush() { /* batch INSERT within transaction */ }
}
```

### Write strategy

1. **Buffer incoming ticks** — accumulate `WriteBufferSize` (default 30) ticks in memory
2. **Batch flush** — wrap all INSERTs in a single transaction (SQLite INSERT performance: ~50k rows/sec in a transaction vs ~60 rows/sec without)
3. **Top-N filtering** — for process snapshots, only persist the top 15 by CPU + top 15 by memory (deduped), keeping total rows per tick manageable
4. **Network filtering** — persist up to 200 active connections per tick (skip LISTEN-only entries if over limit)
5. **Timestamp format** — Unix epoch milliseconds (UTC) stored as INTEGER for fast range queries

### Batch insert pattern

```csharp
private void FlushMetrics()
{
    using var tx = _db.Connection.BeginTransaction();
    using var cmd = _db.Connection.CreateCommand();
    cmd.CommandText = @"INSERT INTO system_metrics
        (ts, cpu_percent, cpu_freq_mhz, cpu_temp_c, process_count, thread_count, handle_count,
         mem_used_bytes, mem_total_bytes, mem_cached_bytes, mem_committed_bytes,
         disk_read_bps, disk_write_bps, net_send_bps, net_recv_bps,
         gpu_percent, gpu_mem_used, gpu_mem_total, gpu_temp_c)
        VALUES
        ($ts, $cpu, $freq, $temp, $procs, $threads, $handles,
         $memUsed, $memTotal, $memCached, $memCommitted,
         $diskR, $diskW, $netS, $netR,
         $gpu, $gpuMem, $gpuMemTotal, $gpuTemp)";

    // Prepare parameters once, rebind per row
    var pTs = cmd.Parameters.Add("$ts", SqliteType.Integer);
    // ... all other parameters ...

    foreach (var m in _metricsBuffer)
    {
        pTs.Value = new DateTimeOffset(m.Timestamp).ToUnixTimeMilliseconds();
        // ... bind all values from m ...
        cmd.ExecuteNonQuery();
    }

    tx.Commit();
    _metricsBuffer.Clear();
}
```

**Commit:** `feat(phase-11): MetricsStore write pipeline with buffered batch inserts`

---

## Task 4: Rollup aggregation and retention pruning

**Files:**
- Create: `src/NexusMonitor.Core/Storage/MetricsRollupService.cs`

This runs on a background timer (every 60 seconds) and handles:

### Rollup logic

1. **1-minute rollups:** Query raw `system_metrics` rows from the last unprocessed minute, compute AVG/MAX, insert into `rollups_1m`
2. **5-minute rollups:** Aggregate from `rollups_1m` into `rollups_5m` every 5 minutes
3. **1-hour rollups:** Aggregate from `rollups_5m` into `rollups_1h` every hour

Track "last processed timestamp" for each rollup level to avoid reprocessing.

### Retention pruning

After rollups, delete old raw data:

```sql
DELETE FROM system_metrics    WHERE ts < $cutoff_raw;      -- older than 1 hour
DELETE FROM process_snapshots WHERE ts < $cutoff_raw;
DELETE FROM network_snapshots WHERE ts < $cutoff_raw;
DELETE FROM rollups_1m        WHERE ts < $cutoff_1m;        -- older than 7 days
DELETE FROM rollups_5m        WHERE ts < $cutoff_5m;        -- older than 30 days
DELETE FROM rollups_1h        WHERE ts < $cutoff_1h;        -- older than 1 year
```

Run `PRAGMA optimize;` periodically (once per hour) to keep query plans efficient.

### Metadata table

```sql
CREATE TABLE IF NOT EXISTS meta (
    key   TEXT PRIMARY KEY,
    value TEXT
);
-- Stores: last_rollup_1m_ts, last_rollup_5m_ts, last_rollup_1h_ts, schema_version
```

**Commit:** `feat(phase-11): rollup aggregation and retention pruning`

---

## Task 5: Read/query API

**Files:**
- Create: `src/NexusMonitor.Core/Storage/IMetricsReader.cs`
- Add query methods to `MetricsStore` (implements `IMetricsReader`)

### IMetricsReader interface

```csharp
public interface IMetricsReader
{
    /// Get system metrics for a time range. Automatically selects the
    /// best granularity (raw, 1m, 5m, 1h) based on range width.
    Task<IReadOnlyList<MetricsDataPoint>> GetSystemMetricsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// Get process history for a specific process name over a time range.
    Task<IReadOnlyList<ProcessDataPoint>> GetProcessHistoryAsync(
        string processName, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// Get network connection history for a remote address/port.
    Task<IReadOnlyList<NetworkDataPoint>> GetNetworkHistoryAsync(
        string? remoteAddress, int? remotePort,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// Get events in a time range, optionally filtered by type.
    Task<IReadOnlyList<StoredEvent>> GetEventsAsync(
        DateTimeOffset from, DateTimeOffset to,
        string? eventType = null, CancellationToken ct = default);

    /// Get the time range of available data.
    Task<(DateTimeOffset oldest, DateTimeOffset newest)> GetDataRangeAsync(
        CancellationToken ct = default);

    /// Get database size in bytes.
    long GetDatabaseSizeBytes();
}
```

### Data point models

```csharp
public record MetricsDataPoint(
    DateTimeOffset Timestamp,
    double CpuPercent, double? CpuMaxPercent,
    long MemUsedBytes, long? MemMaxBytes,
    long DiskReadBps, long DiskWriteBps,
    long NetSendBps, long NetRecvBps,
    double GpuPercent, double? GpuMaxPercent,
    int SampleCount);

public record ProcessDataPoint(
    DateTimeOffset Timestamp,
    int Pid, string Name,
    double CpuPercent, long MemBytes,
    long IoReadBps, long IoWriteBps,
    double GpuPercent);

public record NetworkDataPoint(
    DateTimeOffset Timestamp,
    int Protocol, string LocalAddr, int LocalPort,
    string RemoteAddr, int RemotePort,
    int State, int Pid, string ProcessName,
    long SendBps, long RecvBps);

public record StoredEvent(
    long Id, DateTimeOffset Timestamp,
    string EventType, int Severity,
    string? MetricName, double? MetricValue, double? Threshold,
    string? Description, string? MetadataJson);
```

### Automatic granularity selection

```csharp
// Range < 2 hours  → raw data (1s granularity)
// Range < 24 hours → rollups_1m
// Range < 7 days   → rollups_5m
// Range >= 7 days  → rollups_1h
```

This keeps query result sets manageable regardless of range.

**Commit:** `feat(phase-11): IMetricsReader query API with auto-granularity selection`

---

## Task 6: DI registration and settings integration

**Files:**
- Modify: `src/NexusMonitor.Core/Models/AppSettings.cs` — add metrics config fields
- Modify: `src/NexusMonitor.UI/App.axaml.cs` — register and start MetricsStore

### AppSettings additions

```csharp
// Metrics persistence
public bool   MetricsEnabled           { get; set; } = true;
public int    MetricsTopNProcesses     { get; set; } = 15;
public bool   MetricsRecordNetwork     { get; set; } = true;
public int    MetricsRawRetentionHours { get; set; } = 1;
public int    MetricsRollup1mDays      { get; set; } = 7;
public int    MetricsRollup5mDays      { get; set; } = 30;
public int    MetricsRollup1hDays      { get; set; } = 365;
```

### DI Registration (App.axaml.cs)

```csharp
// Storage — after SettingsService registration
var dbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "NexusMonitor", "metrics.db");
services.AddSingleton(new MetricsDatabase(dbPath));
services.AddSingleton<MetricsStoreConfig>(sp =>
{
    var settings = sp.GetRequiredService<AppSettings>();
    return new MetricsStoreConfig
    {
        TopNProcesses = settings.MetricsTopNProcesses,
        RecordNetworkSnapshots = settings.MetricsRecordNetwork,
        RawRetention = TimeSpan.FromHours(settings.MetricsRawRetentionHours),
        Rollup1mRetention = TimeSpan.FromDays(settings.MetricsRollup1mDays),
        Rollup5mRetention = TimeSpan.FromDays(settings.MetricsRollup5mDays),
        Rollup1hRetention = TimeSpan.FromDays(settings.MetricsRollup1hDays),
    };
});
services.AddSingleton<MetricsStore>();
services.AddSingleton<IMetricsReader>(sp => sp.GetRequiredService<MetricsStore>());
services.AddSingleton<MetricsRollupService>();
```

### Startup (OnFrameworkInitializationCompleted)

After existing service startups:
```csharp
// Start metrics persistence
if (settings.Current.MetricsEnabled)
{
    var store = Services.GetRequiredService<MetricsStore>();
    store.Start(TimeSpan.FromMilliseconds(settings.Current.UpdateIntervalMs));

    var rollups = Services.GetRequiredService<MetricsRollupService>();
    rollups.Start();
}
```

**Commit:** `feat(phase-11): DI registration, settings integration, and startup wiring`

---

## Task 7: Build verification and manual testing

**Steps:**
1. `dotnet build NexusMonitor.sln` — must pass with 0 errors, 0 warnings
2. Run the app for 2+ minutes — verify `metrics.db` is created in `%AppData%/NexusMonitor/`
3. Open `metrics.db` in DB Browser for SQLite (or `sqlite3` CLI):
   - `SELECT COUNT(*) FROM system_metrics;` — should have rows
   - `SELECT COUNT(*) FROM process_snapshots;` — should have rows
   - `SELECT * FROM system_metrics ORDER BY ts DESC LIMIT 5;` — verify data looks correct
4. Let it run for 2+ minutes, then check `rollups_1m` has entries
5. Verify app shutdown is clean (no orphan DB locks)
6. Verify app restart opens existing DB without error

**Commit:** `test(phase-11): verify metrics persistence end-to-end`

---

## File Summary

```
src/NexusMonitor.Core/
├── Storage/                        (NEW directory)
│   ├── MetricsDatabase.cs          (NEW — schema, connection, pragmas)
│   ├── MetricsStore.cs             (NEW — write pipeline, implements IMetricsReader)
│   ├── MetricsStoreConfig.cs       (NEW — configuration model)
│   ├── MetricsRollupService.cs     (NEW — aggregation + retention)
│   ├── IMetricsReader.cs           (NEW — query interface)
│   └── MetricsDataModels.cs        (NEW — MetricsDataPoint, ProcessDataPoint, etc.)
├── Models/
│   └── AppSettings.cs              (MODIFIED — add metrics config fields)
└── NexusMonitor.Core.csproj        (MODIFIED — add Microsoft.Data.Sqlite)

src/NexusMonitor.UI/
└── App.axaml.cs                    (MODIFIED — register + start MetricsStore)
```

**New files:** 6
**Modified files:** 3
**New dependency:** Microsoft.Data.Sqlite 8.0.1

---

## Estimated Database Size

At default settings (1s interval, top-15 processes, 200 network connections):

| Table | Row size | Rows/hour | Rows/day | Size/day |
|-------|---------|-----------|----------|----------|
| system_metrics | ~160 B | 3,600 | 86,400 | ~14 MB |
| process_snapshots | ~100 B | 54,000 | 1,296,000 | ~130 MB |
| network_snapshots | ~120 B | 720,000 | 17,280,000 | ~2 GB |

**With retention pruning (raw = 1 hour):**
- system_metrics: ~0.6 MB steady state
- process_snapshots: ~5.4 MB steady state
- network_snapshots: ~86 MB steady state (or much less with typical connection counts)
- rollups: negligible (1 row/min + 1 row/5min + 1 row/hour)

**Total steady-state:** ~50–100 MB typical, capped by 1-hour raw retention.

---

## What This Enables (Future Phases)

- **Phase 12: In-app historical viewer** — query `IMetricsReader`, render time-range charts with LiveCharts
- **Phase 13: Event & anomaly detection** — write to `events` table, query for patterns
- **Phase 14: Prometheus /metrics endpoint** — read latest from store, expose in exposition format
- **Phase 15: Telegraf integration** — Telegraf scrapes the Prometheus endpoint
- **Phase 16: Grafana dashboards** — JSON templates reading from the same data source
