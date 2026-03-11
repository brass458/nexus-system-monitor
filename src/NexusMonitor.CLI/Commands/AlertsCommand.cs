using NexusMonitor.Core.Alerts;
using NexusMonitor.Core.Services;
using NexusMonitor.Core.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace NexusMonitor.CLI.Commands;

// ── Branch command ──────────────────────────────────────────────────────────

/// <summary>
/// Branch command for alert sub-commands: list, status, watch.
/// </summary>
internal sealed class AlertsCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.MarkupLine("[grey]Usage: nexus alerts [list|status|watch][/]");
        return 0;
    }
}

// ── nexus alerts list ───────────────────────────────────────────────────────

internal sealed class AlertsListCommand : Command
{
    private readonly SettingsService _settings;

    public AlertsListCommand(SettingsService settings)
    {
        _settings = settings;
    }

    public override int Execute(CommandContext context)
    {
        var rules = _settings.Current.AlertRules;
        if (rules.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No alert rules configured.[/]");
            return 0;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("[bold]Name[/]"))
            .AddColumn(new TableColumn("[bold]Metric[/]"))
            .AddColumn(new TableColumn("[bold]Threshold[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Severity[/]"))
            .AddColumn(new TableColumn("[bold]Sustain (s)[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Cooldown (s)[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Enabled[/]"));

        foreach (var r in rules)
        {
            string sevColor = r.Severity switch
            {
                AlertSeverity.Critical => "red",
                AlertSeverity.Warning  => "yellow",
                _                      => "blue",
            };
            table.AddRow(
                Markup.Escape(r.Name),
                r.Metric.ToString(),
                r.Threshold.ToString("F0"),
                $"[{sevColor}]{r.Severity}[/]",
                r.SustainSec.ToString(),
                r.CooldownSec.ToString(),
                r.IsEnabled ? "[green]Yes[/]" : "[grey]No[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

// ── nexus alerts status ─────────────────────────────────────────────────────

internal sealed class AlertsStatusCommand : AsyncCommand
{
    private readonly IEventWriter _eventWriter;
    private readonly AlertsService _alertsService;

    public AlertsStatusCommand(IEventWriter eventWriter, AlertsService alertsService)
    {
        _eventWriter   = eventWriter;
        _alertsService = alertsService;
    }

    public override Task<int> ExecuteAsync(CommandContext context)
    {
        AnsiConsole.MarkupLine($"[bold]Alerts engine:[/] {(_alertsService.IsRunning ? "[green]Running[/]" : "[grey]Stopped[/]")}");
        AnsiConsole.MarkupLine("[grey]Use 'nexus alerts watch' to stream live alert events.[/]");
        return Task.FromResult(0);
    }
}

// ── nexus alerts watch ──────────────────────────────────────────────────────

internal sealed class AlertsWatchCommand : AsyncCommand
{
    private readonly AlertsService   _alerts;
    private readonly SettingsService _settings;

    public AlertsWatchCommand(AlertsService alerts, SettingsService settings)
    {
        _alerts   = alerts;
        _settings = settings;
    }

    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        // Ensure alerts service is running
        if (!_alerts.IsRunning && _settings.Current.AlertRules.Count > 0)
            _alerts.Start();

        AnsiConsole.MarkupLine("[grey]Watching for alerts... Press Ctrl+C to stop.[/]\n");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        using var sub = _alerts.Events.Subscribe(evt =>
        {
            string sevColor = evt.Rule.Severity switch
            {
                AlertSeverity.Critical => "red",
                AlertSeverity.Warning  => "yellow",
                _                      => "blue",
            };
            AnsiConsole.MarkupLine(
                $"[grey]{evt.TimeDisplay}[/]  [{sevColor}]{evt.Rule.Severity}[/]  " +
                $"[bold]{Markup.Escape(evt.Rule.Name)}[/]  {Markup.Escape(evt.Description)}");
        });

        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException) { /* normal exit */ }

        return 0;
    }
}
