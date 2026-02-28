using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
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

    // ── Surface color pickers ─────────────────────────────────────────────────

    /// <summary>Opens the surface color picker pre-seeded with the current window chrome color.</summary>
    private async void OnPickWindowBgClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        vm.PickerSurfaceColor = TryParseColor(vm.CustomWindowBgHex, Color.Parse("#0F0F12"));

        var dlg = new SurfaceColorPickerWindow { DataContext = vm };
        var applied = await dlg.ShowDialog<bool>(owner);
        if (applied)
            vm.CustomWindowBgHex =
                $"#{vm.PickerSurfaceColor.R:X2}{vm.PickerSurfaceColor.G:X2}{vm.PickerSurfaceColor.B:X2}";
    }

    /// <summary>Opens the surface color picker pre-seeded with the current card/panel color.</summary>
    private async void OnPickSurfaceBgClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        vm.PickerSurfaceColor = TryParseColor(vm.CustomSurfaceBgHex, Color.Parse("#1C1C1E"));

        var dlg = new SurfaceColorPickerWindow { DataContext = vm };
        var applied = await dlg.ShowDialog<bool>(owner);
        if (applied)
            vm.CustomSurfaceBgHex =
                $"#{vm.PickerSurfaceColor.R:X2}{vm.PickerSurfaceColor.G:X2}{vm.PickerSurfaceColor.B:X2}";
    }

    /// <summary>Opens the surface color picker pre-seeded with the current sidebar/nav color.</summary>
    private async void OnPickSidebarBgClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        vm.PickerSurfaceColor = TryParseColor(vm.CustomSidebarBgHex, Color.Parse("#1C1C1E"));

        var dlg = new SurfaceColorPickerWindow { DataContext = vm };
        var applied = await dlg.ShowDialog<bool>(owner);
        if (applied)
            vm.CustomSidebarBgHex =
                $"#{vm.PickerSurfaceColor.R:X2}{vm.PickerSurfaceColor.G:X2}{vm.PickerSurfaceColor.B:X2}";
    }

    private static Color TryParseColor(string hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try { return Color.Parse(hex); }
        catch { return fallback; }
    }
}
