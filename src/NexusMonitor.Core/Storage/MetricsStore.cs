using Microsoft.Data.Sqlite;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Storage;

/// <summary>
/// Subscribes to live metric streams and persists them to SQLite in batched transactions.
/// Also implements IMetricsReader for historical queries.
/// </summary>
public sealed class MetricsStore : IMetricsReader, IDisposable
{
    private readonly MetricsDatabase            _db;
    private readonly MetricsStoreConfig         _config;
    private readonly ISystemMetricsProvider     _metricsProvider;
    private readonly IProcessProvider           _processProvider;
    private readonly INetworkConnectionsProvider _networkProvider;

    private IDisposable? _metricsSub;
    private IDisposable? _processSub;
    private IDisposable? _networkSub;

    // Write buffers — guarded by _lock
    private readonly List<SystemMetrics>                               _metricsBuffer = new();
    private readonly List<(long ts, IReadOnlyList<ProcessInfo> procs)> _processBuffer = new();
    private readonly List<(long ts, IReadOnlyList<NetworkConnection> conns)> _networkBuffer = new();
    private readonly object _lock = new();

    private bool _disposed;

    public MetricsStore(
        MetricsDatabase             db,
        MetricsStoreConfig          config,
        ISystemMetricsProvider      metricsProvider,
        IProcessProvider            processProvider,
        INetworkConnectionsProvider networkProvider)
    {
        _db              = db;
        _config          = config;
        _metricsProvider = metricsProvider;
        _processProvider = processProvider;
        _networkProvider = networkProvider;
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────────
    public void Start(TimeSpan interval)
    {
        _metricsSub = _metricsProvider
            .GetMetricsStream(interval)
            .Subscribe(OnMetricsTick);

        _processSub = _processProvider
            .GetProcessStream(interval)
            .Subscribe(procs => OnProcessTick(procs));

        if (_config.RecordNetworkSnapshots)
        {
            _networkSub = _networkProvider
                .GetConnectionStream(TimeSpan.FromSeconds(Math.Max(interval.TotalSeconds, 2)))
                .Subscribe(conns => OnNetworkTick(conns));
        }
    }

    public void Stop()
    {
        _metricsSub?.Dispose();
        _processSub?.Dispose();
        _networkSub?.Dispose();
        lock (_lock) FlushAll();
    }

    // ── Incoming ticks ─────────────────────────────────────────────────────────
    private void OnMetricsTick(SystemMetrics m)
    {
        lock (_lock)
        {
            _metricsBuffer.Add(m);
            if (_metricsBuffer.Count >= _config.WriteBufferSize)
                FlushAll();
        }
    }

    private void OnProcessTick(IReadOnlyList<ProcessInfo> procs)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock (_lock)
            _processBuffer.Add((ts, procs));
    }

    private void OnNetworkTick(IReadOnlyList<NetworkConnection> conns)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock (_lock)
            _networkBuffer.Add((ts, conns));
    }

    // ── Flush ──────────────────────────────────────────────────────────────────
    private void FlushAll()
    {
        // Called inside _lock
        if (_metricsBuffer.Count == 0 && _processBuffer.Count == 0 && _networkBuffer.Count == 0)
            return;

        try
        {
            using var tx = _db.Connection.BeginTransaction();
            FlushMetrics(tx);
            FlushProcesses(tx);
            FlushNetwork(tx);
            tx.Commit();
        }
        catch { /* swallow — never crash the monitoring loop */ }

        _metricsBuffer.Clear();
        _processBuffer.Clear();
        _networkBuffer.Clear();
    }

    private void FlushMetrics(SqliteTransaction tx)
    {
        if (_metricsBuffer.Count == 0) return;

        using var cmd = _db.Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO system_metrics
                (ts, cpu_percent, cpu_freq_mhz, cpu_temp_c, process_count, thread_count, handle_count,
                 mem_used_bytes, mem_total_bytes, mem_cached_bytes, mem_committed_bytes,
                 disk_read_bps, disk_write_bps, net_send_bps, net_recv_bps,
                 gpu_percent, gpu_mem_used, gpu_mem_total, gpu_temp_c)
            VALUES
                ($ts, $cpu, $freq, $temp, $procs, $threads, $handles,
                 $memUsed, $memTotal, $memCached, $memCommitted,
                 $diskR, $diskW, $netS, $netR,
                 $gpu, $gpuMem, $gpuMemTotal, $gpuTemp)";

        var pTs          = cmd.Parameters.Add("$ts",          SqliteType.Integer);
        var pCpu         = cmd.Parameters.Add("$cpu",         SqliteType.Real);
        var pFreq        = cmd.Parameters.Add("$freq",        SqliteType.Real);
        var pTemp        = cmd.Parameters.Add("$temp",        SqliteType.Real);
        var pProcs       = cmd.Parameters.Add("$procs",       SqliteType.Integer);
        var pThreads     = cmd.Parameters.Add("$threads",     SqliteType.Integer);
        var pHandles     = cmd.Parameters.Add("$handles",     SqliteType.Integer);
        var pMemUsed     = cmd.Parameters.Add("$memUsed",     SqliteType.Integer);
        var pMemTotal    = cmd.Parameters.Add("$memTotal",    SqliteType.Integer);
        var pMemCached   = cmd.Parameters.Add("$memCached",   SqliteType.Integer);
        var pMemCommit   = cmd.Parameters.Add("$memCommitted",SqliteType.Integer);
        var pDiskR       = cmd.Parameters.Add("$diskR",       SqliteType.Integer);
        var pDiskW       = cmd.Parameters.Add("$diskW",       SqliteType.Integer);
        var pNetS        = cmd.Parameters.Add("$netS",        SqliteType.Integer);
        var pNetR        = cmd.Parameters.Add("$netR",        SqliteType.Integer);
        var pGpu         = cmd.Parameters.Add("$gpu",         SqliteType.Real);
        var pGpuMem      = cmd.Parameters.Add("$gpuMem",      SqliteType.Integer);
        var pGpuMemTotal = cmd.Parameters.Add("$gpuMemTotal", SqliteType.Integer);
        var pGpuTemp     = cmd.Parameters.Add("$gpuTemp",     SqliteType.Real);

        foreach (var m in _metricsBuffer)
        {
            var diskR = m.Disks.Sum(d => d.ReadBytesPerSec);
            var diskW = m.Disks.Sum(d => d.WriteBytesPerSec);
            var netS  = m.NetworkAdapters.Sum(n => n.SendBytesPerSec);
            var netR  = m.NetworkAdapters.Sum(n => n.RecvBytesPerSec);
            var gpu   = m.Gpus.FirstOrDefault();

            pTs.Value          = new DateTimeOffset(m.Timestamp).ToUnixTimeMilliseconds();
            pCpu.Value         = m.Cpu.TotalPercent;
            pFreq.Value        = m.Cpu.FrequencyMhz;
            pTemp.Value        = m.Cpu.TemperatureCelsius > 0 ? (object)m.Cpu.TemperatureCelsius : DBNull.Value;
            pProcs.Value       = DBNull.Value;
            pThreads.Value     = DBNull.Value;
            pHandles.Value     = DBNull.Value;
            pMemUsed.Value     = m.Memory.UsedBytes;
            pMemTotal.Value    = m.Memory.TotalBytes;
            pMemCached.Value   = m.Memory.CachedBytes;
            pMemCommit.Value   = m.Memory.CommitTotalBytes;
            pDiskR.Value       = diskR;
            pDiskW.Value       = diskW;
            pNetS.Value        = netS;
            pNetR.Value        = netR;
            pGpu.Value         = gpu != null ? (object)gpu.UsagePercent              : DBNull.Value;
            pGpuMem.Value      = gpu != null ? (object)gpu.DedicatedMemoryUsedBytes  : DBNull.Value;
            pGpuMemTotal.Value = gpu != null ? (object)gpu.DedicatedMemoryTotalBytes : DBNull.Value;
            pGpuTemp.Value     = gpu != null && gpu.TemperatureCelsius > 0
                                     ? (object)gpu.TemperatureCelsius : DBNull.Value;

            cmd.ExecuteNonQuery();
        }
    }

    private void FlushProcesses(SqliteTransaction tx)
    {
        if (_processBuffer.Count == 0) return;

        using var cmd = _db.Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO process_snapshots
                (ts, pid, name, cpu_percent, mem_bytes,
                 io_read_bps, io_write_bps, gpu_percent, net_send_bps, net_recv_bps, category)
            VALUES
                ($ts, $pid, $name, $cpu, $mem, $ioR, $ioW, $gpu, $netS, $netR, $cat)";

        var pTs   = cmd.Parameters.Add("$ts",   SqliteType.Integer);
        var pPid  = cmd.Parameters.Add("$pid",  SqliteType.Integer);
        var pName = cmd.Parameters.Add("$name", SqliteType.Text);
        var pCpu  = cmd.Parameters.Add("$cpu",  SqliteType.Real);
        var pMem  = cmd.Parameters.Add("$mem",  SqliteType.Integer);
        var pIoR  = cmd.Parameters.Add("$ioR",  SqliteType.Integer);
        var pIoW  = cmd.Parameters.Add("$ioW",  SqliteType.Integer);
        var pGpu  = cmd.Parameters.Add("$gpu",  SqliteType.Real);
        var pNetS = cmd.Parameters.Add("$netS", SqliteType.Integer);
        var pNetR = cmd.Parameters.Add("$netR", SqliteType.Integer);
        var pCat  = cmd.Parameters.Add("$cat",  SqliteType.Integer);

        foreach (var (ts, procs) in _processBuffer)
        {
            // Top-N by CPU and by memory (deduped)
            var topCpu = procs
                .OrderByDescending(p => p.CpuPercent)
                .Take(_config.TopNProcesses);
            var topMem = procs
                .OrderByDescending(p => p.WorkingSetBytes)
                .Take(_config.TopNProcesses);
            var topN = topCpu.Union(topMem);

            pTs.Value = ts;
            foreach (var p in topN)
            {
                pPid.Value  = p.Pid;
                pName.Value = p.Name;
                pCpu.Value  = p.CpuPercent;
                pMem.Value  = p.WorkingSetBytes;
                pIoR.Value  = p.IoReadBytesPerSec;
                pIoW.Value  = p.IoWriteBytesPerSec;
                pGpu.Value  = p.GpuPercent > 0 ? (object)p.GpuPercent : DBNull.Value;
                pNetS.Value = p.NetworkSendBytesPerSec;
                pNetR.Value = p.NetworkRecvBytesPerSec;
                pCat.Value  = (int)p.Category;
                cmd.ExecuteNonQuery();
            }
        }
    }

    private void FlushNetwork(SqliteTransaction tx)
    {
        if (_networkBuffer.Count == 0) return;

        using var cmd = _db.Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO network_snapshots
                (ts, protocol, local_addr, local_port, remote_addr, remote_port,
                 state, pid, process_name, send_bps, recv_bps)
            VALUES
                ($ts, $proto, $lAddr, $lPort, $rAddr, $rPort,
                 $state, $pid, $name, $send, $recv)";

        var pTs    = cmd.Parameters.Add("$ts",    SqliteType.Integer);
        var pProto = cmd.Parameters.Add("$proto", SqliteType.Integer);
        var pLAddr = cmd.Parameters.Add("$lAddr", SqliteType.Text);
        var pLPort = cmd.Parameters.Add("$lPort", SqliteType.Integer);
        var pRAddr = cmd.Parameters.Add("$rAddr", SqliteType.Text);
        var pRPort = cmd.Parameters.Add("$rPort", SqliteType.Integer);
        var pState = cmd.Parameters.Add("$state", SqliteType.Integer);
        var pPid   = cmd.Parameters.Add("$pid",   SqliteType.Integer);
        var pName  = cmd.Parameters.Add("$name",  SqliteType.Text);
        var pSend  = cmd.Parameters.Add("$send",  SqliteType.Integer);
        var pRecv  = cmd.Parameters.Add("$recv",  SqliteType.Integer);

        foreach (var (ts, conns) in _networkBuffer)
        {
            // Skip LISTEN-only entries if over limit
            var filtered = conns
                .Where(c => c.State != TcpConnectionState.Listen || conns.Count <= _config.NetworkSnapshotMaxRows)
                .Take(_config.NetworkSnapshotMaxRows);

            pTs.Value = ts;
            foreach (var c in filtered)
            {
                pProto.Value = (int)c.Protocol;
                pLAddr.Value = c.LocalAddress;
                pLPort.Value = c.LocalPort;
                pRAddr.Value = c.RemoteAddress;
                pRPort.Value = c.RemotePort;
                pState.Value = (int)c.State;
                pPid.Value   = c.ProcessId;
                pName.Value  = c.ProcessName ?? (object)DBNull.Value;
                pSend.Value  = DBNull.Value;
                pRecv.Value  = DBNull.Value;
                cmd.ExecuteNonQuery();
            }
        }
    }

    // ── IMetricsReader ─────────────────────────────────────────────────────────
    public Task<IReadOnlyList<MetricsDataPoint>> GetSystemMetricsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<MetricsDataPoint>>(() => QuerySystemMetrics(from, to), ct);

    private IReadOnlyList<MetricsDataPoint> QuerySystemMetrics(DateTimeOffset from, DateTimeOffset to)
    {
        var fromMs = from.ToUnixTimeMilliseconds();
        var toMs   = to.ToUnixTimeMilliseconds();
        var span   = to - from;

        // Auto-select granularity
        if (span < TimeSpan.FromHours(2))
            return QueryRaw(fromMs, toMs);
        if (span < TimeSpan.FromDays(1))
            return QueryRollup("rollups_1m", fromMs, toMs);
        if (span < TimeSpan.FromDays(7))
            return QueryRollup("rollups_5m", fromMs, toMs);
        return QueryRollup("rollups_1h", fromMs, toMs);
    }

    private IReadOnlyList<MetricsDataPoint> QueryRaw(long fromMs, long toMs)
    {
        var result = new List<MetricsDataPoint>();
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = @"
            SELECT ts, cpu_percent, mem_used_bytes, mem_total_bytes,
                   disk_read_bps, disk_write_bps, net_send_bps, net_recv_bps,
                   gpu_percent
            FROM system_metrics
            WHERE ts >= $from AND ts <= $to
            ORDER BY ts";
        cmd.Parameters.AddWithValue("$from", fromMs);
        cmd.Parameters.AddWithValue("$to",   toMs);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new MetricsDataPoint(
                Timestamp:     DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                CpuPercent:    reader.GetDouble(1),
                CpuMaxPercent: null,
                MemUsedBytes:  reader.GetInt64(2),
                MemMaxBytes:   null,
                DiskReadBps:   reader.IsDBNull(4) ? 0 : reader.GetInt64(4),
                DiskWriteBps:  reader.IsDBNull(5) ? 0 : reader.GetInt64(5),
                NetSendBps:    reader.IsDBNull(6) ? 0 : reader.GetInt64(6),
                NetRecvBps:    reader.IsDBNull(7) ? 0 : reader.GetInt64(7),
                GpuPercent:    reader.IsDBNull(8) ? 0 : reader.GetDouble(8),
                GpuMaxPercent: null,
                SampleCount:   1));
        }
        return result;
    }

    private IReadOnlyList<MetricsDataPoint> QueryRollup(string table, long fromMs, long toMs)
    {
        var result = new List<MetricsDataPoint>();
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = $@"
            SELECT ts, cpu_avg, cpu_max, mem_avg_bytes, mem_max_bytes,
                   disk_read_avg, disk_write_avg, net_send_avg, net_recv_avg,
                   gpu_avg, gpu_max, sample_count
            FROM {table}
            WHERE ts >= $from AND ts <= $to
            ORDER BY ts";
        cmd.Parameters.AddWithValue("$from", fromMs);
        cmd.Parameters.AddWithValue("$to",   toMs);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new MetricsDataPoint(
                Timestamp:     DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                CpuPercent:    reader.GetDouble(1),
                CpuMaxPercent: reader.IsDBNull(2) ? null : reader.GetDouble(2),
                MemUsedBytes:  reader.GetInt64(3),
                MemMaxBytes:   reader.IsDBNull(4) ? null : reader.GetInt64(4),
                DiskReadBps:   reader.IsDBNull(5) ? 0 : reader.GetInt64(5),
                DiskWriteBps:  reader.IsDBNull(6) ? 0 : reader.GetInt64(6),
                NetSendBps:    reader.IsDBNull(7) ? 0 : reader.GetInt64(7),
                NetRecvBps:    reader.IsDBNull(8) ? 0 : reader.GetInt64(8),
                GpuPercent:    reader.IsDBNull(9) ? 0 : reader.GetDouble(9),
                GpuMaxPercent: reader.IsDBNull(10) ? null : reader.GetDouble(10),
                SampleCount:   reader.IsDBNull(11) ? 0 : reader.GetInt32(11)));
        }
        return result;
    }

    public Task<IReadOnlyList<ProcessDataPoint>> GetProcessHistoryAsync(
        string processName, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<ProcessDataPoint>>(() =>
        {
            var result = new List<ProcessDataPoint>();
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = @"
                SELECT ts, pid, name, cpu_percent, mem_bytes,
                       io_read_bps, io_write_bps, gpu_percent
                FROM process_snapshots
                WHERE name = $name AND ts >= $from AND ts <= $to
                ORDER BY ts";
            cmd.Parameters.AddWithValue("$name", processName);
            cmd.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$to",   to.ToUnixTimeMilliseconds());

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new ProcessDataPoint(
                    Timestamp:  DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                    Pid:        reader.GetInt32(1),
                    Name:       reader.GetString(2),
                    CpuPercent: reader.GetDouble(3),
                    MemBytes:   reader.GetInt64(4),
                    IoReadBps:  reader.IsDBNull(5) ? 0 : reader.GetInt64(5),
                    IoWriteBps: reader.IsDBNull(6) ? 0 : reader.GetInt64(6),
                    GpuPercent: reader.IsDBNull(7) ? 0 : reader.GetDouble(7)));
            }
            return (IReadOnlyList<ProcessDataPoint>)result;
        }, ct);

    public Task<IReadOnlyList<NetworkDataPoint>> GetNetworkHistoryAsync(
        string? remoteAddress, int? remotePort,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<NetworkDataPoint>>(() =>
        {
            var result = new List<NetworkDataPoint>();
            using var cmd = _db.Connection.CreateCommand();

            var where = "ts >= $from AND ts <= $to";
            if (remoteAddress != null) where += " AND remote_addr = $rAddr";
            if (remotePort    != null) where += " AND remote_port = $rPort";

            cmd.CommandText = $@"
                SELECT ts, protocol, local_addr, local_port, remote_addr, remote_port,
                       state, pid, process_name, send_bps, recv_bps
                FROM network_snapshots
                WHERE {where}
                ORDER BY ts";
            cmd.Parameters.AddWithValue("$from",  from.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$to",    to.ToUnixTimeMilliseconds());
            if (remoteAddress != null) cmd.Parameters.AddWithValue("$rAddr", remoteAddress);
            if (remotePort    != null) cmd.Parameters.AddWithValue("$rPort", remotePort.Value);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new NetworkDataPoint(
                    Timestamp:   DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                    Protocol:    reader.GetInt32(1),
                    LocalAddr:   reader.IsDBNull(2) ? "" : reader.GetString(2),
                    LocalPort:   reader.IsDBNull(3) ? 0  : reader.GetInt32(3),
                    RemoteAddr:  reader.IsDBNull(4) ? "" : reader.GetString(4),
                    RemotePort:  reader.IsDBNull(5) ? 0  : reader.GetInt32(5),
                    State:       reader.IsDBNull(6) ? 0  : reader.GetInt32(6),
                    Pid:         reader.IsDBNull(7) ? 0  : reader.GetInt32(7),
                    ProcessName: reader.IsDBNull(8) ? "" : reader.GetString(8),
                    SendBps:     reader.IsDBNull(9) ? 0  : reader.GetInt64(9),
                    RecvBps:     reader.IsDBNull(10) ? 0 : reader.GetInt64(10)));
            }
            return (IReadOnlyList<NetworkDataPoint>)result;
        }, ct);

    public Task<IReadOnlyList<StoredEvent>> GetEventsAsync(
        DateTimeOffset from, DateTimeOffset to,
        string? eventType = null, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<StoredEvent>>(() =>
        {
            var result = new List<StoredEvent>();
            using var cmd = _db.Connection.CreateCommand();
            var where = "ts >= $from AND ts <= $to";
            if (eventType != null) where += " AND event_type = $type";

            cmd.CommandText = $@"
                SELECT id, ts, event_type, severity, metric_name, metric_value,
                       threshold, description, metadata_json
                FROM events
                WHERE {where}
                ORDER BY ts";
            cmd.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$to",   to.ToUnixTimeMilliseconds());
            if (eventType != null) cmd.Parameters.AddWithValue("$type", eventType);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new StoredEvent(
                    Id:           reader.GetInt64(0),
                    Timestamp:    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
                    EventType:    reader.GetString(2),
                    Severity:     reader.GetInt32(3),
                    MetricName:   reader.IsDBNull(4) ? null : reader.GetString(4),
                    MetricValue:  reader.IsDBNull(5) ? null : reader.GetDouble(5),
                    Threshold:    reader.IsDBNull(6) ? null : reader.GetDouble(6),
                    Description:  reader.IsDBNull(7) ? null : reader.GetString(7),
                    MetadataJson: reader.IsDBNull(8) ? null : reader.GetString(8)));
            }
            return (IReadOnlyList<StoredEvent>)result;
        }, ct);

    public Task<(DateTimeOffset oldest, DateTimeOffset newest)> GetDataRangeAsync(
        CancellationToken ct = default) =>
        Task.Run(() =>
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "SELECT MIN(ts), MAX(ts) FROM system_metrics";
            using var reader = cmd.ExecuteReader();
            if (!reader.Read() || reader.IsDBNull(0))
                return (DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            return (
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)));
        }, ct);

    public long GetDatabaseSizeBytes() => _db.GetDatabaseSizeBytes();

    // ── IDisposable ────────────────────────────────────────────────────────────
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
