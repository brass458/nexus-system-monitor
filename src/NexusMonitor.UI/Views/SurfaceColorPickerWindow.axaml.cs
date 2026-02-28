using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace NexusMonitor.UI.Views;

/// <summary>
/// Minimal dialog window for picking a custom background surface color.
/// DataContext must be set to a <see cref="NexusMonitor.UI.ViewModels.SettingsViewModel"/>
/// before the window is shown so the wheel binds to <c>PickerSurfaceColor</c>.
/// Returns <c>true</c> via ShowDialog when the user clicks Apply, <c>false</c> on Cancel.
/// </summary>
public partial class SurfaceColorPickerWindow : Window
{
    public SurfaceColorPickerWindow()
    {
        InitializeComponent();

        // Same transparency hint as ColorPickerWindow — try acrylic, blur, then opaque.
        TransparencyLevelHint =
        [
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Blur,
            WindowTransparencyLevel.None,
        ];
    }

    private void OnApply(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnDragPressed(object? sender, PointerPressedEventArgs e) =>
        BeginMoveDrag(e);
}
