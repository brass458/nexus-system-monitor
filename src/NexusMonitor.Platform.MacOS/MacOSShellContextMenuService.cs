using System.Diagnostics;
using NexusMonitor.Core.Abstractions;

namespace NexusMonitor.Platform.MacOS;

/// <summary>
/// macOS does not support native shell context menus via a COM-equivalent API.
/// The platform UI falls back to an Avalonia ContextMenu.
/// This service provides a "Reveal in Finder" helper used by code-behind.
/// </summary>
public sealed class MacOSShellContextMenuService : IShellContextMenuService
{
    public bool IsSupported => false;

    public void ShowContextMenu(string filePath, nint windowHandle) { }

    /// <summary>Reveals <paramref name="path"/> in Finder (selects the item).</summary>
    public static void RevealInFinder(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo("open", $"-R \"{path}\"")
            {
                UseShellExecute = false,
                CreateNoWindow  = true,
            });
        }
        catch { }
    }
}
