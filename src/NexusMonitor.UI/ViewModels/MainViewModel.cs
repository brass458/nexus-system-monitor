using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using NexusMonitor.Core.Services;
using NexusMonitor.UI.Messages;

namespace NexusMonitor.UI.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    [ObservableProperty]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    private NavItem _selectedNavItem;

    /// <summary>
    /// The sidebar navigation entries.  ObservableCollection so drag-to-reorder
    /// is reflected in the UI without re-creating the list.
    /// </summary>
    public ObservableCollection<NavItem> NavItems { get; } = new();

    private readonly SettingsService _settings;

    public MainViewModel(IServiceProvider services, SettingsService settings)
    {
        _settings = settings;

        // ── Build the default ordered list ─────────────────────────────────
        var defaults = new List<NavItem>
        {
            // eager: true → ViewModel created immediately at startup so its
            // data streams are live before the user ever clicks the tab.
            new NavItem("Processes",    "\ue9f5", () => services.GetRequiredService<ProcessesViewModel>(),    eager: true),
            new NavItem("Performance",  "\ue9d9", () => services.GetRequiredService<PerformanceViewModel>(),  eager: true),
            new NavItem("System Info",  "\ue9d8", () => services.GetRequiredService<SystemInfoViewModel>(),   eager: false),
            new NavItem("Services",     "\ue9a0", () => services.GetRequiredService<ServicesViewModel>(),     eager: false),
            new NavItem("Startup",      "\ue9b0", () => services.GetRequiredService<StartupViewModel>(),      eager: false),
            new NavItem("Network",      "\ue9c8", () => services.GetRequiredService<NetworkViewModel>(),      eager: false),
            new NavItem("Optimization", "\ue993", () => services.GetRequiredService<OptimizationViewModel>(), eager: false),
            new NavItem("ProBalance",   "\ue996", () => services.GetRequiredService<ProBalanceViewModel>(),   eager: false),
            new NavItem("Rules",        "\ue994", () => services.GetRequiredService<RulesViewModel>(),        eager: false),
            new NavItem("Gaming Mode",  "\ue995", () => services.GetRequiredService<GamingModeViewModel>(),   eager: false),
            new NavItem("Alerts",       "\ue997", () => services.GetRequiredService<AlertsViewModel>(),       eager: false),
            new NavItem("Settings",     "\ue992", () => services.GetRequiredService<SettingsViewModel>(),     eager: false),
        };

        // ── Apply saved order (if any) ──────────────────────────────────────
        var savedOrder = settings.Current.NavOrder;
        if (savedOrder.Count > 0)
        {
            var ordered = new List<NavItem>(defaults.Count);
            foreach (var label in savedOrder)
            {
                var item = defaults.FirstOrDefault(n => n.Label == label);
                if (item is not null) ordered.Add(item);
            }
            // Add any new tabs not present in the saved order (e.g., newly added features)
            foreach (var item in defaults.Where(n => !ordered.Contains(n)))
                ordered.Add(item);
            foreach (var item in ordered) NavItems.Add(item);
        }
        else
        {
            foreach (var item in defaults) NavItems.Add(item);
        }

        _selectedNavItem = NavItems[0];
        NavItems[0].IsActive = true;
        _currentPage         = NavItems[0].GetOrCreate();
        Title = "Nexus Monitor";

        WeakReferenceMessenger.Default.Register<NavigateToProcessMessage>(this, (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                var nav = NavItems.First(n => n.Label == "Processes");
                if (SelectedNavItem is not null)
                    SelectedNavItem.IsActive = false;
                SelectedNavItem = nav;
                nav.IsActive    = true;
                CurrentPage     = nav.GetOrCreate();
            });
        });
    }

    [RelayCommand]
    internal void Navigate(NavItem item)
    {
        if (item == SelectedNavItem) return;

        if (SelectedNavItem is not null)
            SelectedNavItem.IsActive = false;

        SelectedNavItem = item;
        item.IsActive   = true;
        CurrentPage     = item.GetOrCreate();
    }

    /// <summary>Persists the current sidebar order to settings.</summary>
    internal void SaveNavOrder()
    {
        _settings.Current.NavOrder = NavItems.Select(n => n.Label).ToList();
        _settings.Save();
    }

    /// <summary>
    /// Disposes every ViewModel that was created during this session.
    /// Called when the main window closes.
    /// </summary>
    public void Dispose()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
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
public sealed class NavItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public string Label { get; }
    public string Icon  { get; }

    private bool _isActive;
    /// <summary>True when this item is the currently selected navigation page.</summary>
    public bool IsActive
    {
        get => _isActive;
        internal set => SetProperty(ref _isActive, value);
    }

    private bool _isDragging;
    /// <summary>True while this item is being dragged to a new position.</summary>
    public bool IsDragging
    {
        get => _isDragging;
        internal set => SetProperty(ref _isDragging, value);
    }

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
