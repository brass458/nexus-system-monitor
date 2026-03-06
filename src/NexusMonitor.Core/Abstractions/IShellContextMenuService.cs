namespace NexusMonitor.Core.Abstractions;

/// <summary>
/// Displays the native OS shell context menu for a file or folder path.
/// </summary>
public interface IShellContextMenuService
{
    /// <summary>True on platforms where shell context menus are supported (Windows only).</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Shows the native shell context menu at the current cursor position.
    /// Blocks until the user makes a selection or dismisses the menu.
    /// </summary>
    void ShowContextMenu(string filePath, nint windowHandle);
}

/// <summary>No-op fallback for non-Windows platforms.</summary>
public sealed class NullShellContextMenuService : IShellContextMenuService
{
    public bool IsSupported => false;
    public void ShowContextMenu(string filePath, nint windowHandle) { }
}
