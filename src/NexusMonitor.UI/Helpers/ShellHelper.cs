using System.Diagnostics;

namespace NexusMonitor.UI.Helpers;

internal static class ShellHelper
{
    public static void OpenFileLocation(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = true
            });
        }
        catch { }
    }

    public static void OpenUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    public static void Launch(string exe)
    {
        if (string.IsNullOrEmpty(exe)) return;
        try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); }
        catch { }
    }
}
