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

        var original = vm.AccentColorHex;
        vm.ActivePickerTarget = SettingsViewModel.ColorPickerTarget.PrimaryAccent;
        try { vm.PickerCurrentColor = Color.Parse(vm.AccentColorHex); } catch { }

        var dlg = new ColorPickerWindow { DataContext = vm };
        var applied = await dlg.ShowDialog<bool>(owner);

        if (!applied)
            vm.AccentColorHex = original;
        vm.ActivePickerTarget = SettingsViewModel.ColorPickerTarget.PrimaryAccent;
    }

    private async void OnPickTextColorClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var originalText = vm.TextAccentColorHex;
        // Pre-seed picker with text accent color (or primary if not set)
        var seedHex = string.IsNullOrEmpty(originalText) ? vm.AccentColorHex : originalText;
        vm.ActivePickerTarget = SettingsViewModel.ColorPickerTarget.TextAccent;
        try { vm.PickerCurrentColor = Color.Parse(seedHex); } catch { }

        var dlg = new ColorPickerWindow { DataContext = vm };
        var applied = await dlg.ShowDialog<bool>(owner);

        vm.ActivePickerTarget = SettingsViewModel.ColorPickerTarget.PrimaryAccent;
        if (!applied)
            vm.TextAccentColorHex = originalText;
        // Restore PickerCurrentColor to primary accent
        try { vm.PickerCurrentColor = Color.Parse(vm.AccentColorHex); } catch { }
    }

    // ── Surface color pickers ─────────────────────────────────────────────────

    /// <summary>Opens ColorPickerWindow pre-seeded with the current window chrome color.</summary>
    private async void OnPickWindowBgClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var original = vm.CustomWindowBgHex;
        vm.ActivePickerTarget = SettingsViewModel.ColorPickerTarget.WindowBg;
        try { vm.PickerCurrentColor = TryParseColor(vm.CustomWindowBgHex, Color.Parse("#0F0F12")); } catch { }

        var dlg = new ColorPickerWindow { DataContext = vm };
        var applied = await dlg.ShowDialog<bool>(owner);

        vm.ActivePickerTarget = SettingsViewModel.ColorPickerTarget.PrimaryAccent;
        if (!applied) vm.CustomWindowBgHex = original;
        try { vm.PickerCurrentColor = Color.Parse(vm.AccentColorHex); } catch { }
    }

    /// <summary>Opens ColorPickerWindow pre-seeded with the current card/panel color.</summary>
    private async void OnPickSurfaceBgClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var original = vm.CustomSurfaceBgHex;
        vm.ActivePickerTarget = SettingsViewModel.ColorPickerTarget.SurfaceBg;
        try { vm.PickerCurrentColor = TryParseColor(vm.CustomSurfaceBgHex, Color.Parse("#1C1C1E")); } catch { }

        var dlg = new ColorPickerWindow { DataContext = vm };
        var applied = await dlg.ShowDialog<bool>(owner);

        vm.ActivePickerTarget = SettingsViewModel.ColorPickerTarget.PrimaryAccent;
        if (!applied) vm.CustomSurfaceBgHex = original;
        try { vm.PickerCurrentColor = Color.Parse(vm.AccentColorHex); } catch { }
    }

    /// <summary>Opens ColorPickerWindow pre-seeded with the current sidebar/nav color.</summary>
    private async void OnPickSidebarBgClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var original = vm.CustomSidebarBgHex;
        vm.ActivePickerTarget = SettingsViewModel.ColorPickerTarget.SidebarBg;
        try { vm.PickerCurrentColor = TryParseColor(vm.CustomSidebarBgHex, Color.Parse("#1C1C1E")); } catch { }

        var dlg = new ColorPickerWindow { DataContext = vm };
        var applied = await dlg.ShowDialog<bool>(owner);

        vm.ActivePickerTarget = SettingsViewModel.ColorPickerTarget.PrimaryAccent;
        if (!applied) vm.CustomSidebarBgHex = original;
        try { vm.PickerCurrentColor = Color.Parse(vm.AccentColorHex); } catch { }
    }

    // ── Save-as-preset dialog ─────────────────────────────────────────────────

    private async void OnSaveAsPresetClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        // Simple inline name dialog
        var nameBox = new TextBox
        {
            Watermark     = "Enter preset name…",
            MinWidth      = 260,
            Margin        = new Avalonia.Thickness(0, 0, 0, 12),
        };

        var okBtn     = new Button { Content = "Save",   Classes = { "nx-btn" }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        var cancelBtn = new Button { Content = "Cancel", Classes = { "nx-btn" }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };

        var btnRow = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
        };
        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(okBtn);

        var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 0 };
        panel.Children.Add(new TextBlock
        {
            Text       = "Save Current Settings As Preset",
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Margin     = new Avalonia.Thickness(0, 0, 0, 10),
        });
        panel.Children.Add(nameBox);
        panel.Children.Add(btnRow);

        var dlg = new Window
        {
            Title              = "Save Theme Preset",
            Content            = panel,
            SizeToContent      = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize          = false,
            ShowInTaskbar      = false,
        };

        string? resultName = null;
        okBtn.Click     += (_, _) => { resultName = nameBox.Text; dlg.Close(); };
        cancelBtn.Click += (_, _) => dlg.Close();
        nameBox.KeyDown += (_, k) =>
        {
            if (k.Key == Avalonia.Input.Key.Enter)  { resultName = nameBox.Text; dlg.Close(); }
            if (k.Key == Avalonia.Input.Key.Escape) dlg.Close();
        };

        await dlg.ShowDialog(owner);

        if (!string.IsNullOrWhiteSpace(resultName))
            vm.SaveCurrentAsPresetCommand.Execute(resultName);
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
