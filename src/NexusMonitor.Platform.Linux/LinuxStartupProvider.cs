using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Platform.Linux;

/// <summary>
/// Enumerates XDG autostart .desktop files from user and system directories.
/// </summary>
public sealed class LinuxStartupProvider : IStartupProvider
{
    private static IEnumerable<string> AutostartDirs
    {
        get
        {
            // User autostart
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrEmpty(configHome))
                yield return Path.Combine(configHome, "autostart");
            else
                yield return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config", "autostart");

            // System-wide XDG autostart dirs
            var xdgDirs = Environment.GetEnvironmentVariable("XDG_CONFIG_DIRS")
                          ?? "/etc/xdg";
            foreach (var d in xdgDirs.Split(':'))
            {
                if (!string.IsNullOrEmpty(d))
                    yield return Path.Combine(d, "autostart");
            }
        }
    }

    public Task<IReadOnlyList<StartupItem>> GetStartupItemsAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<StartupItem>>(Enumerate, ct);

    private static IReadOnlyList<StartupItem> Enumerate()
    {
        var result = new List<StartupItem>();
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in AutostartDirs)
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.desktop"))
                {
                    var baseName = Path.GetFileName(file);
                    if (!seen.Add(baseName)) continue; // user overrides system

                    try
                    {
                        var content = File.ReadAllText(file);
                        var name    = ParseDesktopEntry(content, "Name")
                                   ?? Path.GetFileNameWithoutExtension(file);
                        var exec    = ParseDesktopEntry(content, "Exec") ?? string.Empty;
                        var hidden  = string.Equals(
                            ParseDesktopEntry(content, "Hidden"), "true",
                            StringComparison.OrdinalIgnoreCase);
                        var noDisplay = string.Equals(
                            ParseDesktopEntry(content, "NoDisplay"), "true",
                            StringComparison.OrdinalIgnoreCase);

                        result.Add(new StartupItem
                        {
                            Name      = name,
                            Command   = exec,
                            Publisher = string.Empty,
                            Location  = file,
                            IsEnabled = !hidden && !noDisplay,
                            ItemType  = StartupItemType.StartupFolder,
                        });
                    }
                    catch { }
                }
            }
            catch { }
        }

        return result;
    }

    public Task SetEnabledAsync(StartupItem item, bool enabled, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            if (!File.Exists(item.Location)) return;
            try
            {
                var lines   = File.ReadAllLines(item.Location).ToList();
                var hiddenIdx = lines.FindIndex(l =>
                    l.StartsWith("Hidden=", StringComparison.OrdinalIgnoreCase));

                var newLine = $"Hidden={(!enabled ? "true" : "false")}";
                if (hiddenIdx >= 0)
                    lines[hiddenIdx] = newLine;
                else
                    lines.Add(newLine);

                File.WriteAllLines(item.Location, lines);
            }
            catch { }
        }, ct);

    private static string? ParseDesktopEntry(string content, string key)
    {
        var keyEq = $"{key}=";
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith(keyEq, StringComparison.OrdinalIgnoreCase))
                return trimmed[keyEq.Length..].Trim();
        }
        return null;
    }
}
