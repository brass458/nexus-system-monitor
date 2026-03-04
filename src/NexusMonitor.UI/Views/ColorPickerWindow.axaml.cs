using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using NexusMonitor.UI.ViewModels;

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

    private void OnHexLostFocus(object? sender, RoutedEventArgs e) => CommitHex();

    private void OnHexKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitHex();
    }

    private void CommitHex()
    {
        if (DataContext is not SettingsViewModel vm) return;
        var hex = HexInput.Text?.Trim() ?? "";
        if (!hex.StartsWith('#')) hex = "#" + hex;
        try
        {
            _ = Color.Parse(hex); // validate
            if (vm.TextAccentColorPickerActive)
                vm.TextAccentColorHex = hex;
            else
                vm.AccentColorHex = hex;
        }
        catch
        {
            HexInput.Text = vm.PickerCurrentHex; // revert on invalid
        }
    }
}
