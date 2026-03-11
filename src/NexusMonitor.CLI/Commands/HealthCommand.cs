using NexusMonitor.CLI.Rendering;
using NexusMonitor.Core.Health;
using NexusMonitor.Core.Models;
using NexusMonitor.Core.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace NexusMonitor.CLI.Commands;

/// <summary>
/// Displays a snapshot of the current system health.
/// </summary>
internal sealed class HealthCommand : AsyncCommand
{
    private readonly SystemHealthService _health;
    private readonly SettingsService     _settings;

    public HealthCommand(SystemHealthService health, SettingsService settings)
    {
        _health   = health;
        _settings = settings;
    }

    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        // Start the health service briefly if it isn't already running
        _health.Start(TimeSpan.FromMilliseconds(_settings.Current.UpdateIntervalMs));

        // Give it a tick to produce a non-default snapshot
        var snapshot = _health.Current;
        if (snapshot.OverallScore == 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            snapshot = _health.Current;
        }

        var tree = new Tree($"[bold]System Health — {DateTime.Now:HH:mm:ss}[/]");

        // Overall
        var overallNode = tree.AddNode(
            $"[bold]Overall:[/] {MetricsRenderer.HealthScoreBar(snapshot.OverallScore)}  " +
            $"[grey]{snapshot.OverallHealth}[/]  Trend: {snapshot.OverallTrend}");

        // Subsystems
        AddSubsystemNode(overallNode, snapshot.Cpu,    "CPU");
        AddSubsystemNode(overallNode, snapshot.Memory, "Memory");
        AddSubsystemNode(overallNode, snapshot.Disk,   "Disk");
        AddSubsystemNode(overallNode, snapshot.Gpu,    "GPU");

        // Top consumers
        if (snapshot.TopConsumers.Count > 0)
        {
            var topNode = tree.AddNode("[bold]Top CPU Consumers[/]");
            foreach (var p in snapshot.TopConsumers)
            {
                topNode.AddNode(
                    $"[cyan]{Markup.Escape(p.Name)}[/] (PID {p.Pid})  " +
                    $"CPU: {p.CpuPercent:F1}%  MEM: {MetricsRenderer.FormatBytes(p.MemoryBytes)}  " +
                    $"Impact: {p.ImpactScore:F0}");
            }
        }

        // Bottleneck
        if (snapshot.Bottleneck is { } bn)
        {
            var bnNode = tree.AddNode($"[yellow]Bottleneck: {Markup.Escape(bn.Headline)}[/]");
            if (!string.IsNullOrEmpty(bn.Explanation))
                bnNode.AddNode(Markup.Escape(bn.Explanation));
            if (!string.IsNullOrEmpty(bn.UpgradeAdvice))
                bnNode.AddNode($"[grey]Advice: {Markup.Escape(bn.UpgradeAdvice)}[/]");
        }

        AnsiConsole.Write(tree);
        AnsiConsole.MarkupLine($"\n[grey]Active automations: {snapshot.ActiveAutomations}[/]");

        return 0;
    }

    private static void AddSubsystemNode(TreeNode parent, SubsystemHealth sub, string label)
    {
        parent.AddNode(
            $"[bold]{label}:[/] {MetricsRenderer.ColoredPercentBar(sub.CurrentValue, width: 15)}  " +
            $"Score: {sub.Score:F0}  [{sub.Level}]  Trend: {sub.Trend}" +
            (string.IsNullOrEmpty(sub.Summary) ? "" : $"  — {Markup.Escape(sub.Summary)}"));
    }
}
