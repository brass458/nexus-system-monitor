using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Mock;
using NexusMonitor.Core.Services;
using NexusMonitor.UI.ViewModels;
using NexusMonitor.UI.Views;
#if WINDOWS
using NexusMonitor.Platform.Windows;
#elif MACOS
using NexusMonitor.Platform.MacOS;
#endif

namespace NexusMonitor.UI;

public class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

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
            var overlayVm = Services.GetRequiredService<OverlayViewModel>();
            var overlayWin = new OverlayWindow { DataContext = overlayVm };
            Services.GetRequiredService<SettingsViewModel>().OverlayWindow = overlayWin;

            // Show immediately if the user left it enabled last session
            if (Services.GetRequiredService<SettingsService>().Current.ShowOverlayWidget)
                overlayWin.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        // ── Platform-specific providers ────────────────────────────────────────
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
        // Linux + fallback
        services.AddSingleton<IProcessProvider,             MockProcessProvider>();
        services.AddSingleton<ISystemMetricsProvider,       MockSystemMetricsProvider>();
        services.AddSingleton<IServicesProvider,            MockServicesProvider>();
        services.AddSingleton<INetworkConnectionsProvider,  MockNetworkConnectionsProvider>();
        services.AddSingleton<IStartupProvider,             MockStartupProvider>();
#endif

        // ── Services ───────────────────────────────────────────────────────────
        services.AddSingleton<SettingsService>();

        // ── ViewModels ─────────────────────────────────────────────────────────
        // Singletons: NavItem.GetOrCreate() caches the first-resolved instance for the
        // app lifetime. Transient would silently create a second polling subscription
        // if GetRequiredService were ever called again.
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
