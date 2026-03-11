using System.ComponentModel;
using System.Reflection;
using NexusMonitor.Core.Models;
using NexusMonitor.Core.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace NexusMonitor.CLI.Commands;

// ── Branch command ──────────────────────────────────────────────────────────

/// <summary>
/// Branch command for settings sub-commands: show, get, set.
/// </summary>
internal sealed class SettingsCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.MarkupLine("[grey]Usage: nexus settings [show|get <key>|set <key> <value>][/]");
        return 0;
    }
}

// ── nexus settings show ─────────────────────────────────────────────────────

internal sealed class SettingsShowCommand : Command
{
    private readonly SettingsService _settings;

    public SettingsShowCommand(SettingsService settings)
    {
        _settings = settings;
    }

    public override int Execute(CommandContext context)
    {
        var current = _settings.Current;
        var props   = typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead)
            .OrderBy(p => p.Name);

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("[bold]Key[/]"))
            .AddColumn(new TableColumn("[bold]Value[/]"));

        foreach (var prop in props)
        {
            var val = prop.GetValue(current);
            string display = val switch
            {
                null                => "[grey](null)[/]",
                System.Collections.ICollection col when col.Count == 0 => "[grey](empty list)[/]",
                System.Collections.ICollection col => $"[grey]({col.Count} items)[/]",
                _ => Markup.Escape(val.ToString() ?? "")
            };
            table.AddRow(prop.Name, display);
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

// ── nexus settings get ──────────────────────────────────────────────────────

internal sealed class SettingsGetCommand : Command<SettingsGetCommand.Settings>
{
    private readonly SettingsService _settings;

    public SettingsGetCommand(SettingsService settings)
    {
        _settings = settings;
    }

    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<key>")]
        [Description("The AppSettings property name to read")]
        public string Key { get; init; } = string.Empty;
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var prop = typeof(AppSettings).GetProperty(
            settings.Key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (prop is null)
        {
            AnsiConsole.MarkupLine($"[red]Unknown setting:[/] {Markup.Escape(settings.Key)}");
            return 1;
        }

        var val = prop.GetValue(_settings.Current);
        AnsiConsole.MarkupLine($"[bold]{prop.Name}[/] = {Markup.Escape(val?.ToString() ?? "(null)")}");
        return 0;
    }
}

// ── nexus settings set ──────────────────────────────────────────────────────

internal sealed class SettingsSetCommand : AsyncCommand<SettingsSetCommand.Settings>
{
    private readonly SettingsService _settings;

    public SettingsSetCommand(SettingsService settings)
    {
        _settings = settings;
    }

    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<key>")]
        [Description("The AppSettings property name to set")]
        public string Key { get; init; } = string.Empty;

        [CommandArgument(1, "<value>")]
        [Description("The value to assign (will be converted to the property type)")]
        public string Value { get; init; } = string.Empty;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var prop = typeof(AppSettings).GetProperty(
            settings.Key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (prop is null)
        {
            AnsiConsole.MarkupLine($"[red]Unknown setting:[/] {Markup.Escape(settings.Key)}");
            return 1;
        }

        if (!prop.CanWrite)
        {
            AnsiConsole.MarkupLine($"[red]Property '{prop.Name}' is read-only.[/]");
            return 1;
        }

        try
        {
            object? converted = Convert.ChangeType(settings.Value, prop.PropertyType);
            prop.SetValue(_settings.Current, converted);
            _settings.Save();
            AnsiConsole.MarkupLine($"[green]Set {prop.Name} = {Markup.Escape(settings.Value)}[/]");
            return await Task.FromResult(0);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to set value:[/] {Markup.Escape(ex.Message)}");
            return await Task.FromResult(1);
        }
    }
}
