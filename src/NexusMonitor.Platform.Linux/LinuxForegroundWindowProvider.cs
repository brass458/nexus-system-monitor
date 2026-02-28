using System.Diagnostics;
using NexusMonitor.Core.Automation;

namespace NexusMonitor.Platform.Linux;

/// <summary>
/// Attempts to retrieve the foreground window PID using xdotool.
/// Falls back to 0 if xdotool is not installed or fails.
/// </summary>
public sealed class LinuxForegroundWindowProvider : IForegroundWindowProvider
{
    public int GetForegroundProcessId()
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo("xdotool", "getactivewindow getwindowpid")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                }
            };
            proc.Start();
            var output = proc.StandardOutput.ReadLine();
            proc.WaitForExit(1000);
            if (int.TryParse(output?.Trim(), out var pid))
                return pid;
        }
        catch { }

        return 0;
    }
}
