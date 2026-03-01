using System.Net;
using System.Text;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Telemetry;

/// <summary>
/// Embedded HTTP server that exposes system metrics in Prometheus text format
/// at <c>http://localhost:{port}/metrics</c>. No external dependencies required.
///
/// Usage:
///   Start(port)  — begin listening
///   Stop()       — stop the listener
///   RecordAlertFired() — increment the nexus_alert_events_total counter
/// </summary>
public sealed class PrometheusExporter : IDisposable
{
    private readonly ISystemMetricsProvider _metricsProvider;

    private HttpListener?           _listener;
    private CancellationTokenSource? _cts;
    private Task?                   _loop;
    private long                    _alertEventsTotal;
    private long                    _anomalyEventsTotal;

    public bool IsRunning => _listener is { IsListening: true };

    public PrometheusExporter(ISystemMetricsProvider metricsProvider)
    {
        _metricsProvider = metricsProvider;
    }

    /// <summary>Increments the <c>nexus_alert_events_total</c> counter.
    /// Wire to <c>AlertsService.Events.Subscribe(_ => exporter.RecordAlertFired())</c>.</summary>
    public void RecordAlertFired() => Interlocked.Increment(ref _alertEventsTotal);

    /// <summary>Increments the <c>nexus_anomaly_events_total</c> counter.
    /// Wire to <c>AnomalyDetectionService.AnomalyDetected.Subscribe(_ => exporter.RecordAnomalyDetected())</c>.</summary>
    public void RecordAnomalyDetected() => Interlocked.Increment(ref _anomalyEventsTotal);

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    public void Start(int port)
    {
        if (IsRunning) return;

        _cts      = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[PrometheusExporter] Failed to start on port {port}: {ex.Message}");
            _listener.Close();
            _listener = null;
            return;
        }

        _loop = Task.Run(() => ServeAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { /* suppress */ }
        _listener?.Close();
        _listener = null;
        _loop     = null;
    }

    public void Dispose() => Stop();

    // ── Request loop ───────────────────────────────────────────────────────────

    private async Task ServeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is { IsListening: true })
        {
            HttpListenerContext? ctx = null;
            try
            {
                ctx = await _listener.GetContextAsync().WaitAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch { break; }

            // Handle each request concurrently — do not await here
            _ = Task.Run(async () =>
            {
                try { await HandleRequestAsync(ctx); }
                catch { /* absorb per-request errors */ }
            }, ct);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        var path = ctx.Request.Url?.AbsolutePath ?? string.Empty;
        var resp = ctx.Response;

        if (!path.Equals("/metrics", StringComparison.OrdinalIgnoreCase) &&
            !path.Equals("/metrics/", StringComparison.OrdinalIgnoreCase))
        {
            resp.StatusCode = 404;
            resp.Close();
            return;
        }

        SystemMetrics metrics;
        try
        {
            metrics = await _metricsProvider.GetMetricsAsync();
        }
        catch
        {
            resp.StatusCode = 503;
            resp.Close();
            return;
        }

        var body  = BuildMetricsText(metrics);
        var bytes = Encoding.UTF8.GetBytes(body);

        resp.StatusCode  = 200;
        resp.ContentType = "text/plain; version=0.0.4; charset=utf-8";
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes);
        resp.Close();
    }

    // ── Prometheus text format builder ─────────────────────────────────────────

    private string BuildMetricsText(SystemMetrics m)
    {
        var sb = new StringBuilder(4096);

        // ── CPU ──────────────────────────────────────────────────────────────
        Gauge(sb, "nexus_cpu_usage_percent",      "Total CPU utilization (%)",         m.Cpu.TotalPercent);
        Gauge(sb, "nexus_cpu_frequency_mhz",      "Current CPU frequency (MHz)",       m.Cpu.FrequencyMhz);
        Gauge(sb, "nexus_cpu_temperature_celsius","CPU package temperature (°C)",      m.Cpu.TemperatureCelsius);
        Gauge(sb, "nexus_cpu_process_count",      "Total running processes",           m.Cpu.ProcessCount);
        Gauge(sb, "nexus_cpu_thread_count",       "Total system threads",              m.Cpu.ThreadCount);

        if (m.Cpu.CorePercents.Count > 0)
        {
            Header(sb, "nexus_cpu_core_usage_percent", "gauge", "Per-core CPU utilization (%)");
            for (int i = 0; i < m.Cpu.CorePercents.Count; i++)
                sb.AppendLine($"nexus_cpu_core_usage_percent{{core=\"{i}\"}} {m.Cpu.CorePercents[i]:F2}");
        }

        // ── Memory ───────────────────────────────────────────────────────────
        Gauge(sb, "nexus_memory_used_bytes",         "Physical memory used (bytes)",          m.Memory.UsedBytes);
        Gauge(sb, "nexus_memory_available_bytes",    "Physical memory available (bytes)",     m.Memory.AvailableBytes);
        Gauge(sb, "nexus_memory_total_bytes",        "Physical memory total (bytes)",         m.Memory.TotalBytes);
        Gauge(sb, "nexus_memory_used_percent",       "Physical memory utilization (%)",       m.Memory.UsedPercent);
        Gauge(sb, "nexus_memory_commit_total_bytes", "Committed virtual memory (bytes)",      m.Memory.CommitTotalBytes);
        Gauge(sb, "nexus_memory_commit_limit_bytes", "Commit charge limit (bytes)",           m.Memory.CommitLimitBytes);

        // ── Disks ────────────────────────────────────────────────────────────
        if (m.Disks.Count > 0)
        {
            Header(sb, "nexus_disk_read_bytes_per_second",  "gauge", "Disk read throughput (bytes/s)");
            foreach (var d in m.Disks)
                sb.AppendLine($"nexus_disk_read_bytes_per_second{{disk=\"{Esc(d.DriveLetter)}\"}} {d.ReadBytesPerSec}");

            Header(sb, "nexus_disk_write_bytes_per_second", "gauge", "Disk write throughput (bytes/s)");
            foreach (var d in m.Disks)
                sb.AppendLine($"nexus_disk_write_bytes_per_second{{disk=\"{Esc(d.DriveLetter)}\"}} {d.WriteBytesPerSec}");

            Header(sb, "nexus_disk_active_percent", "gauge", "Disk active time (%)");
            foreach (var d in m.Disks)
                sb.AppendLine($"nexus_disk_active_percent{{disk=\"{Esc(d.DriveLetter)}\"}} {d.ActivePercent:F2}");

            Header(sb, "nexus_disk_used_percent", "gauge", "Disk space used (%)");
            foreach (var d in m.Disks)
                sb.AppendLine($"nexus_disk_used_percent{{disk=\"{Esc(d.DriveLetter)}\"}} {d.UsedPercent:F2}");

            Header(sb, "nexus_disk_free_bytes", "gauge", "Disk space free (bytes)");
            foreach (var d in m.Disks)
                sb.AppendLine($"nexus_disk_free_bytes{{disk=\"{Esc(d.DriveLetter)}\"}} {d.FreeBytes}");

            Header(sb, "nexus_disk_total_bytes", "gauge", "Disk total capacity (bytes)");
            foreach (var d in m.Disks)
                sb.AppendLine($"nexus_disk_total_bytes{{disk=\"{Esc(d.DriveLetter)}\"}} {d.TotalBytes}");
        }

        // ── Network ──────────────────────────────────────────────────────────
        if (m.NetworkAdapters.Count > 0)
        {
            Header(sb, "nexus_network_send_bytes_per_second", "gauge", "Adapter send rate (bytes/s)");
            foreach (var a in m.NetworkAdapters)
                sb.AppendLine($"nexus_network_send_bytes_per_second{{adapter=\"{Esc(a.Name)}\"}} {a.SendBytesPerSec}");

            Header(sb, "nexus_network_recv_bytes_per_second", "gauge", "Adapter receive rate (bytes/s)");
            foreach (var a in m.NetworkAdapters)
                sb.AppendLine($"nexus_network_recv_bytes_per_second{{adapter=\"{Esc(a.Name)}\"}} {a.RecvBytesPerSec}");

            Header(sb, "nexus_network_total_send_bytes", "counter", "Cumulative bytes sent (bytes)");
            foreach (var a in m.NetworkAdapters)
                sb.AppendLine($"nexus_network_total_send_bytes{{adapter=\"{Esc(a.Name)}\"}} {a.TotalSendBytes}");

            Header(sb, "nexus_network_total_recv_bytes", "counter", "Cumulative bytes received (bytes)");
            foreach (var a in m.NetworkAdapters)
                sb.AppendLine($"nexus_network_total_recv_bytes{{adapter=\"{Esc(a.Name)}\"}} {a.TotalRecvBytes}");
        }

        // ── GPU ──────────────────────────────────────────────────────────────
        if (m.Gpus.Count > 0)
        {
            Header(sb, "nexus_gpu_usage_percent", "gauge", "GPU utilization (%)");
            foreach (var g in m.Gpus)
                sb.AppendLine($"nexus_gpu_usage_percent{{gpu=\"{Esc(g.Name)}\"}} {g.UsagePercent:F2}");

            Header(sb, "nexus_gpu_memory_used_bytes", "gauge", "GPU dedicated memory used (bytes)");
            foreach (var g in m.Gpus)
                sb.AppendLine($"nexus_gpu_memory_used_bytes{{gpu=\"{Esc(g.Name)}\"}} {g.DedicatedMemoryUsedBytes}");

            Header(sb, "nexus_gpu_memory_total_bytes", "gauge", "GPU dedicated memory total (bytes)");
            foreach (var g in m.Gpus)
                sb.AppendLine($"nexus_gpu_memory_total_bytes{{gpu=\"{Esc(g.Name)}\"}} {g.DedicatedMemoryTotalBytes}");

            Header(sb, "nexus_gpu_temperature_celsius", "gauge", "GPU temperature (°C)");
            foreach (var g in m.Gpus)
                sb.AppendLine($"nexus_gpu_temperature_celsius{{gpu=\"{Esc(g.Name)}\"}} {g.TemperatureCelsius:F1}");
        }

        // ── Alerts ───────────────────────────────────────────────────────────
        Header(sb, "nexus_alert_events_total", "counter",
            "Total alert threshold breaches since application start");
        sb.AppendLine($"nexus_alert_events_total {Interlocked.Read(ref _alertEventsTotal)}");

        // ── Anomaly Detection ─────────────────────────────────────────────
        Header(sb, "nexus_anomaly_events_total", "counter",
            "Total anomaly events detected since application start");
        sb.AppendLine($"nexus_anomaly_events_total {Interlocked.Read(ref _anomalyEventsTotal)}");

        return sb.ToString();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static void Gauge(StringBuilder sb, string name, string help, double value)
    {
        Header(sb, name, "gauge", help);
        sb.AppendLine($"{name} {value:F2}");
    }

    private static void Gauge(StringBuilder sb, string name, string help, long value)
    {
        Header(sb, name, "gauge", help);
        sb.AppendLine($"{name} {value}");
    }

    private static void Header(StringBuilder sb, string name, string type, string help)
    {
        sb.AppendLine($"# HELP {name} {help}");
        sb.AppendLine($"# TYPE {name} {type}");
    }

    /// <summary>Escapes label values per the Prometheus text format spec.</summary>
    private static string Esc(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
}
