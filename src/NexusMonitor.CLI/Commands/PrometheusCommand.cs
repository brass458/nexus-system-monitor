using System.ComponentModel;
using NexusMonitor.Core.Models;
using NexusMonitor.Core.Telemetry;
using Spectre.Console;
using Spectre.Console.Cli;

namespace NexusMonitor.CLI.Commands;

/// <summary>
/// Starts the Prometheus metrics exporter and blocks until Ctrl+C.
/// </summary>
internal sealed class PrometheusCommand : AsyncCommand<PrometheusCommand.Settings>
{
    private readonly PrometheusExporter _exporter;
    private readonly AppSettings        _appSettings;

    public PrometheusCommand(PrometheusExporter exporter, AppSettings appSettings)
    {
        _exporter    = exporter;
        _appSettings = appSettings;
    }

    internal sealed class Settings : CommandSettings
    {
        [CommandOption("--port")]
        [Description("TCP port for the Prometheus /metrics endpoint (default: from settings or 9182)")]
        public int? Port { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        int port = settings.Port ?? _appSettings.PrometheusPort;

        _exporter.Start(port);
        AnsiConsole.MarkupLine(
            $"[green]Serving metrics at[/] [link=http://localhost:{port}/metrics]http://localhost:{port}/metrics[/]");
        AnsiConsole.MarkupLine("[grey]Press Ctrl+C to stop.[/]");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException) { /* normal exit */ }
        finally
        {
            _exporter.Stop();
            AnsiConsole.MarkupLine("\n[grey]Prometheus exporter stopped.[/]");
        }

        return 0;
    }
}
