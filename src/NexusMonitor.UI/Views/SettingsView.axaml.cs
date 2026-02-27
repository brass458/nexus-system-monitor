using Avalonia.Controls;
using Avalonia.Interactivity;
using NexusMonitor.UI.ViewModels;

namespace NexusMonitor.UI.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    /// <summary>
    /// Opens the <see cref="ColorPickerWindow"/> as a modal dialog.
    /// The wheel binds live to <see cref="SettingsViewModel.PickerAccentColor"/> so the
    /// user sees a real-time preview.  If they cancel, the original hex is restored.
    /// </summary>
    private async void OnPickColorClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        // Snapshot current colour so we can revert on cancel
        var original = vm.AccentColorHex;

        var dlg = new ColorPickerWindow { DataContext = vm };

        var applied = await dlg.ShowDialog<bool>(owner);

        if (!applied)
        {
            // User cancelled — restore the colour that was active before the dialog opened.
            // Use AccentColorHex setter so ApplyAccentColor fires automatically.
            vm.AccentColorHex = original;
        }
    }
}
