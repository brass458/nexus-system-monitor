using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace NexusMonitor.UI.Helpers;

internal static class ClipboardHelper
{
    public static Task CopyAsync(string text)
    {
        if (Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime dt)
            return dt.MainWindow?.Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;
        return Task.CompletedTask;
    }
}
