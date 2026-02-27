using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace NexusMonitor.UI.Views;

/// <summary>
/// Minimal dialog window that hosts the ColorWheelControl in an isolated visual root,
/// preventing any hit-test interference with the SettingsView controls.
/// DataContext must be set to a <see cref="NexusMonitor.UI.ViewModels.SettingsViewModel"/>
/// before the window is shown so the wheel binds to <c>PickerAccentColor</c>.
/// Returns <c>true</c> via ShowDialog when the user clicks Apply, <c>false</c> on Cancel.
/// </summary>
public partial class ColorPickerWindow : Window
{
    public ColorPickerWindow()
    {
        InitializeComponent();

        // Same transparency hint as OverlayWindow — try acrylic, blur, then opaque.
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
