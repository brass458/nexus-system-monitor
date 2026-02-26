using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Mock;
using NexusMonitor.Core.Services;
using NexusMonitor.UI.ViewModels;
using NexusMonitor.UI.Views;
using SkiaSharp;
#if WINDOWS
using NexusMonitor.Platform.Windows;
#elif MACOS
using NexusMonitor.Platform.MacOS;
#endif

namespace NexusMonitor.UI;

public class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    // Keep tray icon alive for the app lifetime
    private TrayIcon? _trayIcon;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Services = BuildServices();

        var saved = Services.GetRequiredService<SettingsService>();
        RequestedThemeVariant = saved.Current.IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainViewModel>()
            };

            // Overlay widget: create the window and wire the Settings toggle
            var overlayVm  = Services.GetRequiredService<OverlayViewModel>();
            var overlayWin = new OverlayWindow { DataContext = overlayVm };
            Services.GetRequiredService<SettingsViewModel>().OverlayWindow = overlayWin;

            // Show immediately if the user left it enabled last session
            if (saved.Current.ShowOverlayWidget)
                overlayWin.Show();

            // System-tray icon
            SetupTrayIcon(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    // ── System-tray icon ──────────────────────────────────────────────────────

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _trayIcon = new TrayIcon
        {
            Icon        = CreateAppIcon(),
            ToolTipText = "Nexus Monitor",
        };

        // Single-click → restore main window
        _trayIcon.Clicked += (_, _) =>
        {
            if (desktop.MainWindow is { } win)
            {
                win.Show();
                win.Activate();
                if (win.WindowState == WindowState.Minimized)
                    win.WindowState = WindowState.Normal;
            }
        };

        var settingsVm = Services.GetRequiredService<SettingsViewModel>();

        var showItem = new NativeMenuItem("Show Nexus Monitor");
        showItem.Click += (_, _) =>
        {
            desktop.MainWindow?.Show();
            desktop.MainWindow?.Activate();
            if (desktop.MainWindow?.WindowState == WindowState.Minimized)
                desktop.MainWindow.WindowState = WindowState.Normal;
        };

        var widgetItem = new NativeMenuItem("Toggle Desktop Widget");
        widgetItem.Click += (_, _) =>
            settingsVm.ShowOverlayWidget = !settingsVm.ShowOverlayWidget;

        var exitItem = new NativeMenuItem("Exit Nexus Monitor");
        exitItem.Click += (_, _) => desktop.Shutdown();

        _trayIcon.Menu = new NativeMenu();
        _trayIcon.Menu.Add(showItem);
        _trayIcon.Menu.Add(new NativeMenuItemSeparator());
        _trayIcon.Menu.Add(widgetItem);
        _trayIcon.Menu.Add(new NativeMenuItemSeparator());
        _trayIcon.Menu.Add(exitItem);

        TrayIcon.SetIcons(this, [_trayIcon]);
    }

    /// <summary>Generates a 32×32 "N" app icon via SkiaSharp — no asset file required.</summary>
    private static WindowIcon CreateAppIcon()
    {
        using var bmp    = new SKBitmap(32, 32);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);

        using var bgPaint = new SKPaint { Color = new SKColor(10, 132, 255), IsAntialias = true };
        canvas.DrawRoundRect(new SKRoundRect(new SKRect(1, 1, 31, 31), 7, 7), bgPaint);

        using var textPaint = new SKPaint
        {
            Color        = SKColors.White,
            IsAntialias  = true,
            TextSize     = 21,
            FakeBoldText = true,
        };
        canvas.DrawText("N", 7f, 24f, textPaint);

        using var img     = SKImage.FromBitmap(bmp);
        using var encoded = img.Encode(SKEncodedImageFormat.Png, 100);
        using var ms      = new System.IO.MemoryStream(encoded.ToArray());
        return new WindowIcon(new Bitmap(ms));
    }

    // ── DI ────────────────────────────────────────────────────────────────────

    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        // ── Platform-specific providers ──────────────────────────────────────
#if WINDOWS
        services.AddSingleton<IProcessProvider,             WindowsProcessProvider>();
        services.AddSingleton<ISystemMetricsProvider,       WindowsSystemMetricsProvider>();
        services.AddSingleton<IServicesProvider,            WindowsServicesProvider>();
        services.AddSingleton<INetworkConnectionsProvider,  WindowsNetworkConnectionsProvider>();
        services.AddSingleton<IStartupProvider,             WindowsStartupProvider>();
#elif MACOS
        services.AddSingleton<IProcessProvider,             MacOSProcessProvider>();
        services.AddSingleton<ISystemMetricsProvider,       MacOSSystemMetricsProvider>();
        services.AddSingleton<IServicesProvider,            MacOSServicesProvider>();
        services.AddSingleton<INetworkConnectionsProvider,  MockNetworkConnectionsProvider>();
        services.AddSingleton<IStartupProvider,             MockStartupProvider>();
#else
        services.AddSingleton<IProcessProvider,             MockProcessProvider>();
        services.AddSingleton<ISystemMetricsProvider,       MockSystemMetricsProvider>();
        services.AddSingleton<IServicesProvider,            MockServicesProvider>();
        services.AddSingleton<INetworkConnectionsProvider,  MockNetworkConnectionsProvider>();
        services.AddSingleton<IStartupProvider,             MockStartupProvider>();
#endif

        // ── Services ─────────────────────────────────────────────────────────
        services.AddSingleton<SettingsService>();

        // ── ViewModels ───────────────────────────────────────────────────────
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<ProcessesViewModel>();
        services.AddSingleton<PerformanceViewModel>();
        services.AddSingleton<ServicesViewModel>();
        services.AddSingleton<StartupViewModel>();
        services.AddSingleton<NetworkViewModel>();
        services.AddSingleton<OptimizationViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<OverlayViewModel>();

        return services.BuildServiceProvider();
    }
}
