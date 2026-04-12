using System.Reactive.Subjects;
using NexusMonitor.Core.Services;

namespace NexusMonitor.UI.Services;

/// <summary>
/// Subject-based implementation of <see cref="IInAppNotificationService"/>.
/// Registered as a singleton; both services and ViewModels may call Show().
/// </summary>
public sealed class InAppNotificationService : IInAppNotificationService, IDisposable
{
    private readonly Subject<InAppNotification> _subject = new();

    public IObservable<InAppNotification> Notifications => _subject;

    /// <inheritdoc/>
    private volatile bool _isSuppressed;
    public bool IsSuppressed
    {
        get => _isSuppressed;
        set => _isSuppressed = value;
    }

    public void Show(InAppNotification notification)
    {
        if (IsSuppressed) return;
        _subject.OnNext(notification);
    }

    public void Dispose() => _subject.Dispose();
}
