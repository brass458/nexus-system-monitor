using System.IO;
using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using NexusMonitor.UI.Helpers;
using NexusMonitor.UI.ViewModels;

namespace NexusMonitor.UI.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();

        var asm = Assembly.GetExecutingAssembly();
        var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                         ?.InformationalVersion
                      ?? asm.GetName().Version?.ToString(3)
                      ?? "0.1.0";
        var plusIndex = version.IndexOf('+');
        if (plusIndex >= 0)
            version = version[..plusIndex];

        AboutVersionText.Text = $"Version {version}";
    }

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

    // ── Setup guide ───────────────────────────────────────────────────────────

    /// <summary>Writes the embedded Telegraf/Grafana setup guide to a temp file and opens it in the default browser.</summary>
    private void OnOpenSetupGuideClick(object? sender, RoutedEventArgs e)
    {
        var uri = new Uri("avares://NexusMonitor/Assets/grafana-setup-guide.html");
        using var stream = AssetLoader.Open(uri);
        using var reader = new StreamReader(stream);
        var html = reader.ReadToEnd();

        var tempPath = Path.Combine(Path.GetTempPath(), "nexus-monitor-setup-guide.html");
        File.WriteAllText(tempPath, html);
        ShellHelper.Launch(tempPath);
    }

    // ── Grafana dashboard export ───────────────────────────────────────────────

    /// <summary>Copies the Grafana dashboard JSON to the system clipboard.</summary>
    private async void OnCopyGrafanaDashboardClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(vm.GrafanaDashboardJson);
    }

    /// <summary>Opens a Save dialog and writes the Grafana dashboard as a .json file.</summary>
    private async void OnSaveGrafanaDashboardClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var sp = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (sp is null) return;

        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title             = "Save Grafana Dashboard",
            SuggestedFileName = "nexus-monitor-dashboard.json",
            DefaultExtension  = "json",
            FileTypeChoices   =
            [
                new FilePickerFileType("Grafana Dashboard JSON") { Patterns = ["*.json"] },
                new FilePickerFileType("All Files")              { Patterns = ["*.*"] },
            ],
        });

        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        await writer.WriteAsync(vm.GrafanaDashboardJson);
    }

    // ── Telegraf config export ─────────────────────────────────────────────────

    /// <summary>Copies the Telegraf config snippet to the system clipboard.</summary>
    private async void OnCopyTelegrafConfigClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(vm.TelegrafConfig);
    }

    /// <summary>Opens a Save dialog and writes the Telegraf config as a .conf file.</summary>
    private async void OnSaveTelegrafConfigClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var sp = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (sp is null) return;

        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title             = "Save Telegraf Configuration",
            SuggestedFileName = "nexus-monitor.conf",
            DefaultExtension  = "conf",
            FileTypeChoices   =
            [
                new FilePickerFileType("Telegraf Config") { Patterns = ["*.conf"] },
                new FilePickerFileType("All Files")       { Patterns = ["*.*"] },
            ],
        });

        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        await writer.WriteAsync(vm.TelegrafConfig);
    }

    // ── About ─────────────────────────────────────────────────────────────────

    private async void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        var dlg = new AboutWindow();
        if (owner is not null)
            await dlg.ShowDialog(owner);
        else
            dlg.Show();
    }

    private static Color TryParseColor(string hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try { return Color.Parse(hex); }
        catch { return fallback; }
    }
}
