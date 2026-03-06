using System.Runtime.InteropServices;
using NexusMonitor.Core.Automation;

namespace NexusMonitor.Platform.MacOS;

/// <summary>
/// Prevents system sleep on macOS using IOKit power management assertions.
/// </summary>
public sealed class MacOSSleepPreventionProvider : ISleepPreventionProvider
{
    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    private static extern int IOPMAssertionCreateWithName(
        string assertionType,
        int assertionLevel,
        string assertionName,
        out uint assertionID);

    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    private static extern int IOPMAssertionRelease(uint assertionID);

    private const string AssertionType  = "PreventUserIdleSystemSleep";
    private const int    AssertionLevel = 255; // kIOPMAssertionLevelOn

    private readonly object _lock = new();
    private uint _assertionId;
    private bool _isActive;

    public void PreventSleep()
    {
        lock (_lock)
        {
            if (_isActive) return;
            try
            {
                var result = IOPMAssertionCreateWithName(
                    AssertionType,
                    AssertionLevel,
                    "NexusMonitor: sleep prevention active",
                    out var id);
                if (result == 0)
                {
                    _assertionId = id;
                    _isActive    = true;
                }
            }
            catch { }
        }
    }

    public void AllowSleep()
    {
        lock (_lock)
        {
            if (!_isActive) return;
            try
            {
                IOPMAssertionRelease(_assertionId);
                _isActive = false;
            }
            catch { }
        }
    }
}
