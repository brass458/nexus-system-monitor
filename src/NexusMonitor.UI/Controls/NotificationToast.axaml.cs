using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using NexusMonitor.Core.Services;

namespace NexusMonitor.UI.Controls;

/// <summary>
/// iOS 26-style animated pill toast notification card.
/// Entry: spring slide-in from top + fade in (Transitions in AXAML).
/// Exit:  fade-out, then raises <see cref="Dismissed"/> for host to remove it.
/// </summary>
public partial class NotificationToast : UserControl
{
    private static readonly Color InfoColor     = Color.Parse("#0A84FF");
    private static readonly Color WarningColor  = Color.Parse("#FF9F0A");
    private static readonly Color CriticalColor = Color.Parse("#FF453A");

    private DispatcherTimer? _dismissTimer;

    /// <summary>Raised when the exit animation completes and the toast should be removed.</summary>
    public event EventHandler? Dismissed;

    public NotificationToast()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Configure the toast and start the entry animation.</summary>
    public void Setup(InAppNotification notification)
    {
        this.FindControl<TextBlock>("TitleText")!.Text = notification.Title;
        this.FindControl<TextBlock>("BodyText")!.Text  = notification.Body;

        var color = notification.Severity switch
        {
            InAppSeverity.Critical => CriticalColor,
            InAppSeverity.Warning  => WarningColor,
            _                      => InfoColor,
        };
        this.FindControl<Border>("AccentBar")!.Background = new SolidColorBrush(color);

        // Trigger entry animation on next layout pass
        AttachedToVisualTree += (_, _) => AnimateIn();

        if (notification.AutoDismiss > TimeSpan.Zero)
        {
            _dismissTimer = new DispatcherTimer { Interval = notification.AutoDismiss };
            _dismissTimer.Tick += (_, _) => AnimateOut();
            _dismissTimer.Start();
        }
    }

    private void OnDismissClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _dismissTimer?.Stop();
        AnimateOut();
    }

    private void AnimateIn()
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Slide to Y=0 (Transition in AXAML handles spring easing)
            var border = this.FindControl<Border>("RootBorder")!;
            if (border.RenderTransform is TranslateTransform t)
                t.Y = 0;

            // Fade in
            Opacity = 1.0;
        }, DispatcherPriority.Render);
    }

    private void AnimateOut()
    {
        _dismissTimer?.Stop();
        Dispatcher.UIThread.Post(() =>
        {
            Opacity = 0.0;
            // Wait for fade-out transition (250ms) then fire Dismissed
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Dismissed?.Invoke(this, EventArgs.Empty);
            };
            timer.Start();
        });
    }
}
