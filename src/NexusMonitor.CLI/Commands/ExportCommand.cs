using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using NexusMonitor.Core.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace NexusMonitor.CLI.Commands;

/// <summary>
/// Exports historical metrics to CSV or JSON format.
/// </summary>
internal sealed class ExportCommand : AsyncCommand<ExportCommand.Settings>
{
    private readonly IMetricsReader _reader;

    public ExportCommand(IMetricsReader reader)
    {
        _reader = reader;
    }

    internal sealed class Settings : CommandSettings
    {
        [CommandOption("--format")]
        [Description("Output format: csv or json (default: csv)")]
        [DefaultValue("csv")]
        public string Format { get; init; } = "csv";

        [CommandOption("--last")]
        [Description("Time span to export, e.g. 1h, 24h, 7h (default: 1h)")]
        [DefaultValue("1h")]
        public string Last { get; init; } = "1h";

        [CommandOption("--output")]
        [Description("Output file path. If omitted, writes to stdout.")]
        public string? Output { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        // Parse the --last duration (e.g. "1h", "24h", "7h")
        double hours = ParseHours(settings.Last);
        var to   = DateTimeOffset.UtcNow;
        var from = to - TimeSpan.FromHours(hours);

        AnsiConsole.MarkupLine($"[grey]Querying metrics from {from:u} to {to:u}...[/]");

        IReadOnlyList<MetricsDataPoint> data;
        try
        {
            data = await _reader.GetSystemMetricsAsync(from, to);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Query failed:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        if (data.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No data found for the specified time range.[/]");
            return 0;
        }

        string content = settings.Format.ToLowerInvariant() switch
        {
            "json" => SerializeJson(data),
            _      => SerializeCsv(data),
        };

        if (string.IsNullOrEmpty(settings.Output))
        {
            Console.Write(content);
        }
        else
        {
            await File.WriteAllTextAsync(settings.Output, content);
            AnsiConsole.MarkupLine($"[green]Exported {data.Count} rows to {Markup.Escape(settings.Output)}[/]");
        }

        return 0;
    }

    private static double ParseHours(string last)
    {
        if (string.IsNullOrWhiteSpace(last)) return 1.0;
        var trimmed = last.Trim().ToLowerInvariant();
        if (trimmed.EndsWith('h') &&
            double.TryParse(trimmed[..^1], NumberStyles.Any, CultureInfo.InvariantCulture, out double h))
            return h;
        if (double.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out double plain))
            return plain;
        return 1.0;
    }

    private static string SerializeCsv(IReadOnlyList<MetricsDataPoint> data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,CpuPercent,MemUsedBytes,DiskReadBps,DiskWriteBps,NetSendBps,NetRecvBps,GpuPercent,SampleCount");
        foreach (var p in data)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{p.Timestamp:O},{p.CpuPercent:F2},{p.MemUsedBytes},{p.DiskReadBps},{p.DiskWriteBps},{p.NetSendBps},{p.NetRecvBps},{p.GpuPercent:F2},{p.SampleCount}"));
        }
        return sb.ToString();
    }

    private static string SerializeJson(IReadOnlyList<MetricsDataPoint> data)
    {
        return JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }
}
