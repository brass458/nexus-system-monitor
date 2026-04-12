using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using NexusMonitor.Core.Abstractions;
using Serilog;
using NexusMonitor.Core.Automation;
using NexusMonitor.Core.Models;
using NexusMonitor.Core.Alerts;
using System.Reactive;
using System.Reactive.Disposables;
using NexusMonitor.Core.Gaming;
using NexusMonitor.Core.Rules;
using NexusMonitor.Core.Services;
using NexusMonitor.Core.Storage;
using NexusMonitor.Core.Telemetry;
using NexusMonitor.UI.Controls;
using NexusMonitor.UI.Services;
using NexusMonitor.Core.Health;
using NexusMonitor.UI.ViewModels;
using NexusMonitor.UI.Views;
using NexusMonitor.Hosting;

namespace NexusMonitor.UI;

public class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    // Keep tray icon alive for the app lifetime
    private TrayIcon? _trayIcon;

    // 4F: Rx subscriptions that must be disposed on shutdown
    private readonly CompositeDisposable _subscriptions = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // ── Log any exception that escapes to the Avalonia UI-thread dispatcher ──
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Log.Fatal(e.Exception, "Unhandled exception on Avalonia UI thread");
            CrashLogger.Write(e.Exception, "Avalonia Dispatcher UI Thread");
            // Do NOT set e.Handled = true here — let Avalonia's default crash behaviour run
            // so the user isn't left with a silent, frozen window.
        };

        Services = BuildServices();

        var saved = Services.GetRequiredService<SettingsService>();
        RequestedThemeVariant = saved.Current.ThemeMode switch
        {
            "Dark"  => ThemeVariant.Dark,
            "Light" => ThemeVariant.Light,
            _       => DetectSystemTheme(),
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainViewModel>()
            };
            desktop.MainWindow = mainWindow;

            // Attach in-app notification host to service
            var inAppService = Services.GetRequiredService<IInAppNotificationService>();
            mainWindow.FindControl<NotificationHost>("AppNotificationHost")?.Attach(inAppService);

            // Overlay widget: create the window and wire the Settings toggle
            var overlayVm  = Services.GetRequiredService<OverlayViewModel>();
            var overlayWin = new OverlayWindow { DataContext = overlayVm };
            Services.GetRequiredService<SettingsViewModel>().OverlayWindow = overlayWin;

            // Show immediately if the user left it enabled last session
            if (saved.Current.ShowOverlayWidget)
                overlayWin.Show();

            // Start health service (always on — powers the Dashboard tab)
            Services.GetRequiredService<SystemHealthService>()
                .Start(TimeSpan.FromMilliseconds(saved.Current.UpdateIntervalMs));

            // Start automation engines
            // ProBalance only starts if it was enabled in settings
            if (saved.Current.ProBalanceEnabled)
                Services.GetRequiredService<ProBalanceService>().Start();

            // Start rules engine if rules exist OR there are saved process preferences
            var prefStore = Services.GetRequiredService<ProcessPreferenceStore>();
            if (saved.Current.Rules.Count > 0 || prefStore.GetAll().Count > 0)
                Services.GetRequiredService<RulesEngine>().Start();
            if (saved.Current.AlertRules.Count > 0)
                Services.GetRequiredService<AlertsService>().Start();

            // Start metrics persistence and incident monitoring
            if (saved.Current.MetricsEnabled)
            {
                Services.GetRequiredService<MetricsStore>().Start(
                    TimeSpan.FromMilliseconds(saved.Current.UpdateIntervalMs));
                Services.GetRequiredService<MetricsRollupService>().Start();
                Services.GetRequiredService<EventMonitorService>().Start();
                Services.GetRequiredService<HealthSnapshotPersistenceService>().Start();
            }

            // Start anomaly detection if enabled
            var anomalyService = Services.GetRequiredService<AnomalyDetectionService>();
            if (saved.Current.AnomalyDetectionEnabled)
                anomalyService.Start();

            // Start memory leak detection if enabled
            if (saved.Current.MemoryLeakDetectionEnabled)
                Services.GetRequiredService<MemoryLeakDetectionService>().Start();

            // Restore active performance profile if one was saved
            if (saved.Current.ActiveProfileId.HasValue)
                Services.GetRequiredService<PerformanceProfileService>()
                    .ActivateProfile(saved.Current.ActiveProfileId.Value);

            // Start Phase 18 automation services
            Services.GetRequiredService<SleepPreventionService>().Start();
            if (saved.Current.ForegroundBoostEnabled)
                Services.GetRequiredService<ForegroundBoostService>().Start();
            if (saved.Current.IdleSaverEnabled)
                Services.GetRequiredService<IdleSaverService>().Start();
            if (saved.Current.SmartTrimEnabled)
                Services.GetRequiredService<SmartTrimService>().Start();
            if (saved.Current.CpuLimiterEnabled)
                Services.GetRequiredService<CpuLimiterService>().Start();
            if (saved.Current.InstanceBalancerEnabled)
                Services.GetRequiredService<InstanceBalancerService>().Start();
            if (saved.Current.QuietHoursEnabled)
                Services.GetRequiredService<QuietHoursService>().Start();

            // Start smart glass adaptive service if enabled
            if (saved.Current.SmartTintEnabled)
                Services.GetRequiredService<GlassAdaptiveService>().Start();

            // Start Prometheus endpoint if enabled
            var prometheusExporter = Services.GetRequiredService<PrometheusExporter>();
            if (saved.Current.PrometheusEnabled)
                prometheusExporter.Start(saved.Current.PrometheusPort);

            // 4A: Flush MetricsStore + dispose services on shutdown so the last buffered
            //     data points are persisted and Rx subscriptions are released cleanly.
            desktop.ShutdownRequested += (_, _) =>
            {
                // Remove tray icon immediately so it doesn't ghost after process exit
                _trayIcon?.Dispose();
                _trayIcon = null;

                // Automation services (hold OS handles / modified process state)
                Services.GetService<ForegroundBoostService>()?.Stop();
                Services.GetService<IdleSaverService>()?.Stop();
                Services.GetService<SmartTrimService>()?.Stop();
                Services.GetService<CpuLimiterService>()?.Stop();
                Services.GetService<InstanceBalancerService>()?.Stop();
                Services.GetService<QuietHoursService>()?.Stop();
                Services.GetService<SleepPreventionService>()?.Stop();
                Services.GetService<GamingModeService>()?.Stop();
                Services.GetService<ProBalanceService>()?.Stop();
                Services.GetService<PerformanceProfileService>()?.DeactivateProfile();
                Services.GetService<MemoryLeakDetectionService>()?.Stop();

                // Monitoring services
                Services.GetService<AlertsService>()?.Stop();
                Services.GetService<AnomalyDetectionService>()?.Stop();
                Services.GetService<RulesEngine>()?.Stop();

                // Data persistence (flush buffers last)
                Services.GetRequiredService<EventMonitorService>().Stop();
                Services.GetService<HealthSnapshotPersistenceService>()?.Stop();
                Services.GetService<MetricsRollupService>()?.Stop();
                Services.GetRequiredService<MetricsStore>().Stop();
                Services.GetRequiredService<SystemHealthService>().Stop();

                // Infrastructure
                Services.GetService<GlassAdaptiveService>()?.Stop();
                Services.GetService<PrometheusExporter>()?.Stop();

                _subscriptions.Dispose();
                (Services as IDisposable)?.Dispose();
            };

            // Wire alert events → Prometheus counter
            var alertsService = Services.GetRequiredService<AlertsService>();
            _subscriptions.Add(alertsService.Events.Subscribe(_ => prometheusExporter.RecordAlertFired()));

            // Wire alert events → events table (bridge)
            var eventWriter = Services.GetRequiredService<IEventWriter>();
            _subscriptions.Add(alertsService.Events.Subscribe(alert =>
            {
                var eventType = alert.Rule.Metric switch
                {
                    AlertMetric.CpuPercent  => EventType.CpuHigh,
                    AlertMetric.RamPercent  => EventType.MemHigh,
                    AlertMetric.GpuPercent  => EventType.GpuHigh,
                    _                       => "alert"
                };
                var severity = alert.Rule.Severity switch
                {
                    AlertSeverity.Critical => EventSeverity.Critical,
                    AlertSeverity.Warning  => EventSeverity.Warning,
                    _                      => EventSeverity.Info
                };
                _ = eventWriter.InsertEventAsync(
                    eventType, severity,
                    alert.Rule.Metric.ToString().ToLowerInvariant(), alert.Value,
                    alert.Rule.Threshold, alert.Description);
            }));

            // Wire anomaly detection → Prometheus counter + OS + in-app notifications
            var notificationService     = Services.GetRequiredService<INotificationService>();
            var inAppNotifications      = Services.GetRequiredService<IInAppNotificationService>();

            // Wire QuietHoursService → IsSuppressed on IInAppNotificationService
            var quietHours = Services.GetRequiredService<QuietHoursService>();
            inAppNotifications.IsSuppressed = quietHours.IsActive;
            _subscriptions.Add(quietHours.IsActiveChanged.Subscribe(active => inAppNotifications.IsSuppressed = active));

            _subscriptions.Add(anomalyService.AnomalyDetected.Subscribe(evt =>
            {
                // Re-check enabled state at fire time (user may have toggled it off)
                if (!saved.Current.AnomalyDetectionEnabled) return;

                // Prometheus counters: total + per-type
                prometheusExporter.RecordAnomalyDetected(evt.EventType);

                // OS desktop notification (gated on desktop notifications setting)
                if (saved.Current.DesktopNotificationsEnabled && notificationService.IsSupported)
                    notificationService.ShowAnomaly(
                        evt.EventType,
                        evt.Description ?? string.Empty,
                        evt.Severity);

                // In-app pill notification (gated on anomaly notifications setting)
                if (saved.Current.AnomalyNotificationsEnabled)
                {
                    var inAppSeverity = evt.Severity switch
                    {
                        EventSeverity.Critical => InAppSeverity.Critical,
                        EventSeverity.Warning  => InAppSeverity.Warning,
                        _                      => InAppSeverity.Info,
                    };
                    inAppNotifications.Show(new InAppNotification(
                        Title:       $"Anomaly \u2014 {evt.EventType}",
                        Body:        evt.Description ?? string.Empty,
                        Severity:    inAppSeverity,
                        AutoDismiss: TimeSpan.FromSeconds(5)));
                }
            }));

            // System-tray icon (only if enabled)
            if (saved.Current.MinimizeToTray)
                SetupTrayIcon(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    // ── System-tray icon ────────────────────────────────────────────

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _trayIcon = new TrayIcon
        {
            Icon        = CreateAppIcon(),
            ToolTipText = "Nexus Monitor",
        };

        // Single-click -> restore main window
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

        var aboutItem = new NativeMenuItem("About Nexus System Monitor");
        aboutItem.Click += (_, _) =>
        {
            var dlg = new AboutWindow();
            if (desktop.MainWindow is { } owner)
                dlg.ShowDialog(owner);
            else
                dlg.Show();
        };

        var exitItem = new NativeMenuItem("Exit Nexus Monitor");
        exitItem.Click += (_, _) =>
        {
            MainWindow.ForceQuitFromTray = true;
            desktop.Shutdown();
        };

        _trayIcon.Menu = new NativeMenu();
        _trayIcon.Menu.Add(showItem);
        _trayIcon.Menu.Add(new NativeMenuItemSeparator());
        _trayIcon.Menu.Add(widgetItem);
        _trayIcon.Menu.Add(new NativeMenuItemSeparator());
        _trayIcon.Menu.Add(aboutItem);
        _trayIcon.Menu.Add(new NativeMenuItemSeparator());
        _trayIcon.Menu.Add(exitItem);

        TrayIcon.SetIcons(this, [_trayIcon]);
    }

    /// <summary>Loads the embedded Nexus Hub app icon from the Assets directory.</summary>
    private static WindowIcon CreateAppIcon()
    {
        using var stream = AssetLoader.Open(new Uri("avares://NexusMonitor/Assets/nexus-icon-256.png"));
        return new WindowIcon(stream);
    }

    // ── System theme detection ──────────────────────────────────────────────

    /// <summary>
    /// Returns the OS dark/light preference. Falls back to Dark on any failure.
    /// </summary>
    public static ThemeVariant DetectSystemTheme()
    {
        try
        {
            var v = Current?.PlatformSettings?.GetColorValues().ThemeVariant;
            return v == PlatformThemeVariant.Light ? ThemeVariant.Light : ThemeVariant.Dark;
        }
        catch { return ThemeVariant.Dark; }
    }

    // -- DI --

    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddNexusPlatformProviders();
        services.AddNexusCoreServices();

        // UI-only registrations
        services.AddSingleton<InAppNotificationService>();
        services.AddSingleton<IInAppNotificationService>(sp =>
            sp.GetRequiredService<InAppNotificationService>());
        services.AddSingleton<GlassAdaptiveService>();

        // -- ViewModels --
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ProcessesViewModel>();
        services.AddSingleton<PerformanceViewModel>();
        services.AddSingleton<SystemInfoViewModel>();
        services.AddSingleton<ServicesViewModel>();
        services.AddSingleton<StartupViewModel>();
        services.AddSingleton<NetworkViewModel>();
        services.AddSingleton<OptimizationViewModel>();
        services.AddSingleton<ProBalanceViewModel>();
        services.AddSingleton<RulesViewModel>();
        services.AddSingleton<GamingModeViewModel>();
        services.AddSingleton<AlertsViewModel>();
        services.AddSingleton<DiskAnalyzerViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<OverlayViewModel>();
        services.AddSingleton<LanScannerViewModel>();
        services.AddSingleton<PerformanceProfilesViewModel>();
        services.AddSingleton<AutomationViewModel>();
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<ProcessGroupsViewModel>();

        return services.BuildServiceProvider();
    }
}
