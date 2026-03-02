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

    public void Show(InAppNotification notification) => _subject.OnNext(notification);

    public void Dispose() => _subject.Dispose();
}
