using NexusMonitor.Core.Models;
using Spectre.Console;

namespace NexusMonitor.CLI.Rendering;

/// <summary>
/// Builds a Spectre.Console <see cref="Table"/> for displaying process lists.
/// </summary>
internal static class ProcessTableRenderer
{
    /// <summary>
    /// Builds a formatted table for the given process list.
    /// </summary>
    /// <param name="processes">The process list to display.</param>
    /// <param name="topN">If &gt; 0, only the first <paramref name="topN"/> rows are shown.</param>
    public static Table BuildTable(IReadOnlyList<ProcessInfo> processes, int topN = 0)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("[bold]PID[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Name[/]"))
            .AddColumn(new TableColumn("[bold]CPU%[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Memory[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Threads[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]State[/]"));

        int count = topN > 0 ? Math.Min(topN, processes.Count) : processes.Count;
        for (int i = 0; i < count; i++)
        {
            var p = processes[i];
            string cpuColor = p.CpuPercent > 50 ? "red" : p.CpuPercent > 20 ? "yellow" : "green";
            table.AddRow(
                p.Pid.ToString(),
                Markup.Escape(p.Name),
                $"[{cpuColor}]{p.CpuPercent:F1}[/]",
                MetricsRenderer.FormatBytes(p.WorkingSetBytes),
                p.ThreadCount.ToString(),
                p.State.ToString());
        }

        return table;
    }
}
