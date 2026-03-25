using System.Reactive.Linq;
using NexusMonitor.CLI.Infrastructure;
using NexusMonitor.CLI.Rendering;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Health;
using NexusMonitor.Core.Models;
using NexusMonitor.Core.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace NexusMonitor.CLI.Commands;

/// <summary>
/// Default command — shows a live-refreshing system dashboard.
/// Press Q to quit.
/// </summary>
internal sealed class DashboardCommand : AsyncCommand
{
    private readonly ISystemMetricsProvider _metrics;
    private readonly IProcessProvider       _processes;
    private readonly SystemHealthService    _health;
    private readonly SettingsService        _settings;

    public DashboardCommand(
        ISystemMetricsProvider metrics,
        IProcessProvider       processes,
        SystemHealthService    health,
        SettingsService        settings)
    {
        _metrics   = metrics;
        _processes = processes;
        _health    = health;
        _settings  = settings;
    }

    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Program.GlobalCts.Token);

        // Allow Q key to cancel
        _ = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    if (key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape)
                    {
                        cts.Cancel();
                        break;
                    }
                }
                Thread.Sleep(50);
            }
        });

        // Latest snapshot references updated by Rx subscriptions
        SystemMetrics? latestMetrics = null;
        IReadOnlyList<ProcessInfo> latestProcesses = Array.Empty<ProcessInfo>();

        var interval = TimeSpan.FromSeconds(2);
        _health.Start(interval);

        using var metricsSub = _metrics.GetMetricsStream(interval)
            .Subscribe(m => Volatile.Write(ref latestMetrics, m));

        using var processSub = _processes.GetProcessStream(interval)
            .Subscribe(list => Volatile.Write(ref latestProcesses, list));

        AnsiConsole.Clear();

        await AnsiConsole.Live(new Text("Initialising dashboard..."))
            .AutoClear(false)
            .StartAsync(async ctx =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var snapshot = _health.Current;
                        SystemMetrics? metrics = Volatile.Read(ref latestMetrics);
                        var procs    = Volatile.Read(ref latestProcesses)
                                       ?? Array.Empty<ProcessInfo>();

                        var layout = BuildLayout(snapshot, metrics, procs);
                        ctx.UpdateTarget(layout);

                        await Task.Delay(500, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            });

        return 0;
    }

    private static Grid BuildLayout(
        SystemHealthSnapshot snapshot,
        SystemMetrics?       metrics,
        IReadOnlyList<ProcessInfo> procs)
    {
        var grid = new Grid();
        grid.AddColumn();

        // Header
        grid.AddRow(new Rule("[bold cyan]Nexus System Monitor — Dashboard[/]") { Justification = Justify.Left });
        grid.AddRow(new Text($"  {DateTime.Now:yyyy-MM-dd HH:mm:ss}  |  Press Q to quit", new Style(Color.Grey)));

        // Health score
        grid.AddRow(new Markup(
            $"\n  [bold]Overall Health:[/] {MetricsRenderer.HealthScoreBar(snapshot.OverallScore)} " +
            $"  [grey]{snapshot.OverallHealth}[/]  Trend: [grey]{snapshot.OverallTrend}[/]"));

        // Metric bars
        if (metrics is not null)
        {
            grid.AddRow(new Markup(
                $"  [bold]CPU:[/]  {MetricsRenderer.ColoredPercentBar(metrics.Cpu.TotalPercent)}" +
                $"   [bold]MEM:[/]  {MetricsRenderer.ColoredPercentBar(metrics.Memory.UsedPercent)}"));

            double diskBusy = metrics.Disks.Count > 0 ? metrics.Disks.Max(d => d.ActivePercent) : 0;
            double gpuBusy  = metrics.Gpus.Count > 0 ? metrics.Gpus.Max(g => g.UsagePercent) : 0;
            grid.AddRow(new Markup(
                $"  [bold]DISK:[/] {MetricsRenderer.ColoredPercentBar(diskBusy)}" +
                $"   [bold]GPU:[/]  {MetricsRenderer.ColoredPercentBar(gpuBusy)}"));
        }
        else
        {
            grid.AddRow(new Markup("  [grey]Waiting for metrics...[/]"));
        }

        // Bottleneck
        if (snapshot.Bottleneck is { } bn && !string.IsNullOrEmpty(bn.Headline))
        {
            grid.AddRow(new Markup($"\n  [yellow]Bottleneck:[/] {Markup.Escape(bn.Headline)}"));
        }

        // Top 10 processes
        grid.AddRow(new Rule("[bold]Top 10 Processes[/]") { Justification = Justify.Left });
        var sorted = procs.OrderByDescending(p => p.CpuPercent).ToList();
        grid.AddRow(ProcessTableRenderer.BuildTable(sorted, topN: 10));

        // Automation status
        grid.AddRow(new Markup($"  [grey]Active automations: {snapshot.ActiveAutomations}[/]"));

        return grid;
    }
}
