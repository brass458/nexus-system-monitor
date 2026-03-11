using NexusMonitor.Core.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace NexusMonitor.CLI.Commands;

// ── Branch command ──────────────────────────────────────────────────────────

/// <summary>
/// Branch command for rules sub-commands: list, status.
/// </summary>
internal sealed class RulesCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.MarkupLine("[grey]Usage: nexus rules [list|status][/]");
        return 0;
    }
}

// ── nexus rules list ────────────────────────────────────────────────────────

internal sealed class RulesListCommand : Command
{
    private readonly SettingsService _settings;

    public RulesListCommand(SettingsService settings)
    {
        _settings = settings;
    }

    public override int Execute(CommandContext context)
    {
        var rules = _settings.Current.Rules;
        if (rules.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No process rules configured.[/]");
            return 0;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("[bold]Name[/]"))
            .AddColumn(new TableColumn("[bold]Pattern[/]"))
            .AddColumn(new TableColumn("[bold]Priority[/]"))
            .AddColumn(new TableColumn("[bold]Affinity Mask[/]"))
            .AddColumn(new TableColumn("[bold]Enabled[/]"));

        foreach (var r in rules)
        {
            table.AddRow(
                Markup.Escape(r.Name),
                Markup.Escape(r.ProcessNamePattern),
                r.Priority?.ToString() ?? "[grey]-[/]",
                r.AffinityMask.HasValue ? $"0x{r.AffinityMask.Value:X}" : "[grey]-[/]",
                r.IsEnabled ? "[green]Yes[/]" : "[grey]No[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

// ── nexus rules status ──────────────────────────────────────────────────────

internal sealed class RulesStatusCommand : Command
{
    private readonly SettingsService _settings;

    public RulesStatusCommand(SettingsService settings)
    {
        _settings = settings;
    }

    public override int Execute(CommandContext context)
    {
        int ruleCount = _settings.Current.Rules.Count;
        // RulesEngine doesn't expose an IsRunning property; show rule count as a proxy
        AnsiConsole.MarkupLine($"[bold]Rules engine:[/] [grey](see rule count for status)[/]");
        AnsiConsole.MarkupLine($"[bold]Configured rules:[/] {ruleCount}");
        AnsiConsole.MarkupLine(ruleCount > 0
            ? "[green]Rules engine will start automatically when nexus is running.[/]"
            : "[grey]No rules — engine will not start automatically.[/]");
        return 0;
    }
}
