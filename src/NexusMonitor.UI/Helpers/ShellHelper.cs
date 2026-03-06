using System.Diagnostics;

namespace NexusMonitor.UI.Helpers;

internal static class ShellHelper
{
    public static void OpenFileLocation(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                {
                    UseShellExecute = true
                });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo("open", $"-R \"{path}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow  = true,
                });
            }
            else
            {
                var dir = Path.GetDirectoryName(path) ?? path;
                Process.Start(new ProcessStartInfo("xdg-open", $"\"{dir}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow  = true,
                });
            }
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
