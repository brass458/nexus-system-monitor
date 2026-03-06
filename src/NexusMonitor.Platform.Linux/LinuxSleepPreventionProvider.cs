using System.Diagnostics;
using NexusMonitor.Core.Automation;

namespace NexusMonitor.Platform.Linux;

/// <summary>
/// Prevents system sleep on Linux by holding a systemd-inhibit lock.
/// Falls back silently if systemd-inhibit is not available.
/// </summary>
public sealed class LinuxSleepPreventionProvider : ISleepPreventionProvider, IDisposable
{
    private readonly object _lock = new();
    private Process? _inhibitProcess;

    public void PreventSleep()
    {
        lock (_lock)
        {
            if (_inhibitProcess is not null) return;
            try
            {
                var psi = new ProcessStartInfo("systemd-inhibit",
                    "--what=idle:sleep --who=NexusMonitor --why=\"User requested sleep prevention\" --mode=block cat")
                {
                    UseShellExecute        = false,
                    RedirectStandardInput  = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow         = true,
                };
                _inhibitProcess = Process.Start(psi);
            }
            catch
            {
                // systemd-inhibit not available — no-op
                _inhibitProcess = null;
            }
        }
    }

    public void AllowSleep()
    {
        lock (_lock)
        {
            if (_inhibitProcess is null) return;
            try
            {
                if (!_inhibitProcess.HasExited)
                    _inhibitProcess.Kill();
                _inhibitProcess.Dispose();
            }
            catch { }
            finally { _inhibitProcess = null; }
        }
    }

    public void Dispose() => AllowSleep();
}
