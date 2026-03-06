using System.Runtime.InteropServices;
using NexusMonitor.Core.Automation;

namespace NexusMonitor.Platform.Windows;

/// <summary>
/// Uses SetThreadExecutionState to prevent Windows from sleeping while a
/// matching process is running.
/// </summary>
public sealed class WindowsSleepPreventionProvider : ISleepPreventionProvider
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);

    private const uint ES_CONTINUOUS      = 0x80000000u;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001u;

    public void PreventSleep()
    {
        SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);
    }

    public void AllowSleep()
    {
        SetThreadExecutionState(ES_CONTINUOUS);
    }
}
