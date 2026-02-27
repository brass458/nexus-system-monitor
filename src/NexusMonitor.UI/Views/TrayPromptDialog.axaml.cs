using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NexusMonitor.UI.Views;

/// <summary>Result from the close-prompt dialog.</summary>
public record TrayPromptResult(string Action, bool Remember, bool HideWidget);

public partial class TrayPromptDialog : Window
{
    private readonly bool _widgetVisible;

    // Parameterless constructor required by Avalonia's XAML runtime loader (AVLN3001)
    public TrayPromptDialog() : this(false) { }

    public TrayPromptDialog(bool widgetVisible)
    {
        InitializeComponent();
        _widgetVisible = widgetVisible;
        // Show widget option only when the overlay widget is currently active
        HideWidgetCheck.IsVisible = widgetVisible;
    }

    private void OnMinimizeToTray(object? sender, RoutedEventArgs e) =>
        Close(new TrayPromptResult(
            "Tray",
            RememberCheck.IsChecked == true,
            _widgetVisible && HideWidgetCheck.IsChecked == true));

    private void OnCloseApp(object? sender, RoutedEventArgs e) =>
        Close(new TrayPromptResult(
            "Exit",
            RememberCheck.IsChecked == true,
            _widgetVisible && HideWidgetCheck.IsChecked == true));
}
