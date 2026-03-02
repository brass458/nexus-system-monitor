using Avalonia.Controls;
using Avalonia.Threading;
using NexusMonitor.Core.Services;

namespace NexusMonitor.UI.Controls;

/// <summary>
/// Overlay host anchored to the top-right of the main window.
/// Subscribes to <see cref="IInAppNotificationService"/> and
/// manages up to 3 visible <see cref="NotificationToast"/> instances;
/// additional notifications are queued.
/// </summary>
public partial class NotificationHost : UserControl
{
    private const int MaxVisible = 3;

    private IDisposable? _subscription;
    private readonly Queue<InAppNotification> _queue = new();

    public NotificationHost()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Attach to the notification service. Called from App.axaml.cs after DI is built.</summary>
    public void Attach(IInAppNotificationService service)
    {
        _subscription?.Dispose();
        _subscription = service.Notifications.Subscribe(OnNotification);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _subscription?.Dispose();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnNotification(InAppNotification notification)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var stack = this.FindControl<StackPanel>("ToastStack")!;
            if (stack.Children.Count >= MaxVisible)
            {
                _queue.Enqueue(notification);
                return;
            }
            ShowToast(notification, stack);
        });
    }

    private void ShowToast(InAppNotification notification, StackPanel stack)
    {
        var toast = new NotificationToast { IsHitTestVisible = true };
        toast.Setup(notification);
        toast.Dismissed += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                stack.Children.Remove(toast);
                if (_queue.Count > 0)
                    ShowToast(_queue.Dequeue(), stack);
            });
        };
        stack.Children.Add(toast);
    }
}
