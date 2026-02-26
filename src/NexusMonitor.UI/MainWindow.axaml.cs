using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace NexusMonitor.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Request acrylic/blur transparency — set in code since XAML type conversion differs across Avalonia versions
        TransparencyLevelHint = [
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Blur,
            WindowTransparencyLevel.Mica,
            WindowTransparencyLevel.None
        ];
        // Dispose all cached ViewModels (and their Rx subscriptions / kernel handles)
        // when the window closes. Without this, PerformanceCounters, SCM handles, etc. leak.
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximize(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
