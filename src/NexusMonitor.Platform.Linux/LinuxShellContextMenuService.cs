using System.Diagnostics;
using NexusMonitor.Core.Abstractions;

namespace NexusMonitor.Platform.Linux;

/// <summary>
/// Linux does not support native shell context menus.
/// The platform UI falls back to an Avalonia ContextMenu.
/// This service provides an "Open in File Manager" helper used by code-behind.
/// </summary>
public sealed class LinuxShellContextMenuService : IShellContextMenuService
{
    public bool IsSupported => false;

    public void ShowContextMenu(string filePath, nint windowHandle) { }

    /// <summary>Opens the parent directory of <paramref name="path"/> in the default file manager.</summary>
    public static void RevealInFileManager(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            var dir = Path.GetDirectoryName(path) ?? path;
            Process.Start(new ProcessStartInfo("xdg-open", $"\"{dir}\"")
            {
                UseShellExecute = false,
                CreateNoWindow  = true,
            });
        }
        catch { }
    }
}
