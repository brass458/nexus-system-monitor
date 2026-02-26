using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace NexusMonitor.UI.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    [ObservableProperty]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    private NavItem _selectedNavItem;

    public IReadOnlyList<NavItem> NavItems { get; }

    public MainViewModel(IServiceProvider services)
    {
        NavItems =
        [
            // eager: true  →  ViewModel created immediately at app startup so its
            // data streams are live before the user ever clicks the tab.
            new NavItem("Processes",    "\ue9f5", () => services.GetRequiredService<ProcessesViewModel>(),   eager: true),
            new NavItem("Performance",  "\ue9d9", () => services.GetRequiredService<PerformanceViewModel>(), eager: true),
            new NavItem("Services",     "\ue9a0", () => services.GetRequiredService<ServicesViewModel>(),    eager: false),
            new NavItem("Startup",      "\ue9b0", () => services.GetRequiredService<StartupViewModel>(),     eager: false),
            new NavItem("Network",      "\ue9c8", () => services.GetRequiredService<NetworkViewModel>(),     eager: false),
            new NavItem("Optimization", "\ue993", () => services.GetRequiredService<OptimizationViewModel>(), eager: false),
            new NavItem("Settings",     "\ue992", () => services.GetRequiredService<SettingsViewModel>(),    eager: false),
        ];

        _selectedNavItem = NavItems[0];
        _currentPage     = NavItems[0].GetOrCreate();   // Processes VM already exists
        Title = "Nexus Monitor";
    }

    [RelayCommand]
    private void Navigate(NavItem item)
    {
        if (item == SelectedNavItem) return;

        // Each NavItem caches its ViewModel — we never dispose on navigation.
        // The instance keeps its Rx subscription and history alive in the
        // background, so data is always ready when the user returns to a tab.
        SelectedNavItem = item;
        CurrentPage     = item.GetOrCreate();
    }

    /// <summary>
    /// Disposes every ViewModel that was created during this session.
    /// Called when the main window closes.
    /// </summary>
    public void Dispose()
    {
        foreach (var item in NavItems)
            item.DisposeViewModel();
    }
}

/// <summary>
/// Represents a sidebar navigation entry.
/// Owns a single ViewModel instance for the lifetime of the app.
/// If <paramref name="eager"/> is <see langword="true"/>, the ViewModel is
/// created immediately (and its data streams start) rather than waiting for
/// the user to navigate to the tab.
/// </summary>
public sealed class NavItem
{
    public string Label { get; }
    public string Icon  { get; }

    private readonly Func<ViewModelBase> _factory;
    private ViewModelBase? _cached;

    public NavItem(string label, string icon, Func<ViewModelBase> factory, bool eager = false)
    {
        Label    = label;
        Icon     = icon;
        _factory = factory;

        if (eager)
            GetOrCreate();   // start data streams immediately
    }

    /// <summary>
    /// Returns the cached ViewModel, creating it on first call.
    /// Subsequent calls always return the same instance.
    /// </summary>
    public ViewModelBase GetOrCreate() => _cached ??= _factory();

    /// <summary>Disposes the cached ViewModel if it was created and implements IDisposable.</summary>
    public void DisposeViewModel() => (_cached as IDisposable)?.Dispose();
}
