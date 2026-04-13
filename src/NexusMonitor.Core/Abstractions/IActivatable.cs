namespace NexusMonitor.Core.Abstractions;

/// <summary>Implemented by ViewModels that can suspend/resume their data stream subscriptions.</summary>
public interface IActivatable
{
    void Activate();
    void Deactivate();
}
