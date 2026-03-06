using System.Diagnostics;
using NexusMonitor.Core.Automation;

namespace NexusMonitor.Platform.MacOS;

/// <summary>
/// Prevents system sleep on macOS by holding a <c>caffeinate -di</c> subprocess.
/// Uses the same subprocess-hold pattern as LinuxSleepPreventionProvider to avoid
/// the CFStringRef marshalling issue with IOPMAssertionCreateWithName P/Invoke.
/// </summary>
public sealed class MacOSSleepPreventionProvider : ISleepPreventionProvider, IDisposable
{
    private readonly object _lock = new();
    private Process? _caffeinateProcess;

    public void PreventSleep()
    {
        lock (_lock)
        {
            if (_caffeinateProcess is not null) return;
            try
            {
                var psi = new ProcessStartInfo("caffeinate", "-di")
                {
                    UseShellExecute        = false,
                    RedirectStandardInput  = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow         = true,
                };
                _caffeinateProcess = Process.Start(psi);
            }
            catch
            {
                // caffeinate not available — no-op
                _caffeinateProcess = null;
            }
        }
    }

    public void AllowSleep()
    {
        lock (_lock)
        {
            if (_caffeinateProcess is null) return;
            try
            {
                if (!_caffeinateProcess.HasExited)
                    _caffeinateProcess.Kill();
                _caffeinateProcess.Dispose();
            }
            catch { }
            finally { _caffeinateProcess = null; }
        }
    }

    public void Dispose() => AllowSleep();
}
