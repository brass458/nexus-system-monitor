namespace NexusMonitor.Core.Automation;

/// <summary>
/// Abstracts the OS-level "prevent sleep" API so the service layer remains
/// platform-independent.
/// </summary>
public interface ISleepPreventionProvider
{
    /// <summary>Tell the OS not to sleep (keep display + system awake).</summary>
    void PreventSleep();

    /// <summary>Release the sleep-prevention request, allowing the OS to sleep normally.</summary>
    void AllowSleep();
}

/// <summary>No-op fallback for platforms without sleep-prevention support.</summary>
public sealed class NullSleepPreventionProvider : ISleepPreventionProvider
{
    public void PreventSleep() { }
    public void AllowSleep()   { }
}
