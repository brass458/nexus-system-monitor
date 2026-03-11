using System.ComponentModel;
using NexusMonitor.CLI.Rendering;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace NexusMonitor.CLI.Commands;

/// <summary>
/// Lists processes. Supports one-shot, live, and interactive modes.
/// </summary>
internal sealed class ProcessesCommand : AsyncCommand<ProcessesCommand.Settings>
{
    private readonly IProcessProvider _processes;

    public ProcessesCommand(IProcessProvider processes)
    {
        _processes = processes;
    }

    internal sealed class Settings : CommandSettings
    {
        [CommandOption("--sort")]
        [Description("Sort field: cpu|mem|name (default: cpu)")]
        [DefaultValue("cpu")]
        public string Sort { get; init; } = "cpu";

        [CommandOption("--filter")]
        [Description("Filter processes by name (case-insensitive substring)")]
        public string? Filter { get; init; }

        [CommandOption("--top")]
        [Description("Number of processes to display (default: 20)")]
        [DefaultValue(20)]
        public int Top { get; init; } = 20;

        [CommandOption("--live")]
        [Description("Continuously refresh the process list")]
        [DefaultValue(false)]
        public bool Live { get; init; }

        [CommandOption("--interactive")]
        [Description("Pick a process interactively and perform an action")]
        [DefaultValue(false)]
        public bool Interactive { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (settings.Interactive)
            return await RunInteractiveAsync(settings);

        if (settings.Live)
            return await RunLiveAsync(settings);

        return await RunOneShotAsync(settings);
    }

    private async Task<int> RunOneShotAsync(Settings settings)
    {
        var procs = await _processes.GetProcessesAsync();
        var filtered = ApplyFilterAndSort(procs, settings);
        AnsiConsole.Write(ProcessTableRenderer.BuildTable(filtered, settings.Top));
        return 0;
    }

    private async Task<int> RunLiveAsync(Settings settings)
    {
        using var cts = new CancellationTokenSource();
        _ = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                if (Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.Q)
                {
                    cts.Cancel();
                    break;
                }
                Thread.Sleep(50);
            }
        });

        IReadOnlyList<ProcessInfo> latest = Array.Empty<ProcessInfo>();
        using var sub = _processes.GetProcessStream(TimeSpan.FromSeconds(2))
            .Subscribe(list => Volatile.Write(ref latest, list));

        await AnsiConsole.Live(new Text("Loading processes..."))
            .AutoClear(false)
            .StartAsync(async ctx =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var current = Volatile.Read(ref latest)
                                      ?? Array.Empty<ProcessInfo>();
                        var filtered = ApplyFilterAndSort(current, settings);
                        var table    = ProcessTableRenderer.BuildTable(filtered, settings.Top);
                        ctx.UpdateTarget(table);
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

    private async Task<int> RunInteractiveAsync(Settings settings)
    {
        var procs = await _processes.GetProcessesAsync();
        var filtered = ApplyFilterAndSort(procs, settings);

        if (filtered.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No processes found.[/]");
            return 0;
        }

        // Show selection
        var choices = filtered.Take(settings.Top)
            .Select(p => $"{p.Pid,6}  {p.Name,-40}  CPU: {p.CpuPercent,5:F1}%  MEM: {MetricsRenderer.FormatBytes(p.WorkingSetBytes)}")
            .ToList();

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a process:")
                .PageSize(20)
                .AddChoices(choices));

        int pidIndex = filtered.Take(settings.Top)
            .Select((p, i) => (p, i))
            .First(x => choices[x.i] == selected).p.Pid;

        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Choose action:")
                .AddChoices("Kill", "Set Priority (High)", "Set Priority (Normal)", "Set Priority (Below Normal)",
                             "Trim Working Set", "Enable Efficiency Mode", "Disable Efficiency Mode",
                             "Suspend", "Resume", "Cancel"));

        switch (action)
        {
            case "Kill":
                await _processes.KillProcessAsync(pidIndex);
                AnsiConsole.MarkupLine($"[green]Sent kill to PID {pidIndex}.[/]");
                break;
            case "Set Priority (High)":
                await _processes.SetPriorityAsync(pidIndex, ProcessPriority.High);
                AnsiConsole.MarkupLine($"[green]Priority set to High for PID {pidIndex}.[/]");
                break;
            case "Set Priority (Normal)":
                await _processes.SetPriorityAsync(pidIndex, ProcessPriority.Normal);
                AnsiConsole.MarkupLine($"[green]Priority set to Normal for PID {pidIndex}.[/]");
                break;
            case "Set Priority (Below Normal)":
                await _processes.SetPriorityAsync(pidIndex, ProcessPriority.BelowNormal);
                AnsiConsole.MarkupLine($"[green]Priority set to Below Normal for PID {pidIndex}.[/]");
                break;
            case "Trim Working Set":
                await _processes.TrimWorkingSetAsync(pidIndex);
                AnsiConsole.MarkupLine($"[green]Trimmed working set for PID {pidIndex}.[/]");
                break;
            case "Enable Efficiency Mode":
                await _processes.SetEfficiencyModeAsync(pidIndex, true);
                AnsiConsole.MarkupLine($"[green]Efficiency mode enabled for PID {pidIndex}.[/]");
                break;
            case "Disable Efficiency Mode":
                await _processes.SetEfficiencyModeAsync(pidIndex, false);
                AnsiConsole.MarkupLine($"[green]Efficiency mode disabled for PID {pidIndex}.[/]");
                break;
            case "Suspend":
                await _processes.SuspendProcessAsync(pidIndex);
                AnsiConsole.MarkupLine($"[green]Suspended PID {pidIndex}.[/]");
                break;
            case "Resume":
                await _processes.ResumeProcessAsync(pidIndex);
                AnsiConsole.MarkupLine($"[green]Resumed PID {pidIndex}.[/]");
                break;
            default:
                AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
                break;
        }

        return 0;
    }

    private static IReadOnlyList<ProcessInfo> ApplyFilterAndSort(
        IReadOnlyList<ProcessInfo> procs, Settings settings)
    {
        IEnumerable<ProcessInfo> q = procs;

        if (!string.IsNullOrEmpty(settings.Filter))
            q = q.Where(p => p.Name.Contains(settings.Filter, StringComparison.OrdinalIgnoreCase));

        q = settings.Sort switch
        {
            "mem"  => q.OrderByDescending(p => p.WorkingSetBytes),
            "name" => q.OrderBy(p => p.Name),
            _      => q.OrderByDescending(p => p.CpuPercent),
        };

        return q.ToList();
    }
}
