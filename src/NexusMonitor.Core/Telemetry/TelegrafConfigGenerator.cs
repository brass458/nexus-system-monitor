namespace NexusMonitor.Core.Telemetry;

/// <summary>
/// Generates a ready-to-use Telegraf TOML configuration snippet that
/// configures <c>inputs.prometheus</c> to scrape the Nexus Monitor endpoint.
///
/// Drop the output into <c>telegraf.conf</c> or save it as a separate file
/// in Telegraf's <c>conf.d</c> directory.
/// </summary>
public static class TelegrafConfigGenerator
{
    public static string Generate(int port)
    {
        var url = $"http://localhost:{port}/metrics";

        return $"""
            # Nexus Monitor — Telegraf Input Configuration
            # ─────────────────────────────────────────────────────────────────
            # Option A: paste the [[inputs.prometheus]] block below into your
            #           existing telegraf.conf under the [inputs] section.
            #
            # Option B: save this entire file as nexus-monitor.conf inside the
            #           Telegraf conf.d directory (e.g. /etc/telegraf/telegraf.d/
            #           or C:\Program Files\Telegraf\telegraf.d\).
            #
            # Telegraf docs:  https://docs.influxdata.com/telegraf/
            # Plugin docs:    https://github.com/influxdata/telegraf/tree/master/plugins/inputs/prometheus
            # ─────────────────────────────────────────────────────────────────

            [[inputs.prometheus]]
              ## Nexus System Monitor Prometheus endpoint
              urls = ["{url}"]

              ## Scrape interval — override the global agent interval (optional)
              # interval = "15s"

              ## HTTP timeout
              response_timeout = "5s"

              ## metric_version=2 uses the Prometheus metric name directly as the
              ## field key and puts labels into Telegraf tags (recommended).
              metric_version = 2

              ## Tag every metric with the application source for easy filtering
              [inputs.prometheus.tags]
                source = "nexus-monitor"
            """;
    }
}
