# Phase 3 — Polish & Settings Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task.

**Goal:** Complete the remaining half-built features, add functional Settings persistence with theme switching, wire Process priority commands, and add Network connection state badges.

**Architecture:** Five independent tasks touching separate layers. All tasks follow existing patterns: MVVM (CommunityToolkit), ReactiveUI observables, Avalonia XAML bindings. No new NuGet packages needed. Build verification: `dotnet build NexusMonitor.sln` — must stay at 0 errors, 0 warnings.

**Tech Stack:** C# 12 / .NET 8, Avalonia 11.2, CommunityToolkit.Mvvm 8.x, System.Text.Json (already in .NET BCL)

**Working directory:** `O:\Github\Nexus System Monitor`

**Build command:** `dotnet build NexusMonitor.sln`

---

## Task 1: StartupView — Add LastError to status bar

**Goal:** The `StartupViewModel` already has `LastError` (added in code quality fixes) but the view never shows it. Consistent with `ProcessesView` and `ServicesView`.

**Files:**
- Modify: `src/NexusMonitor.UI/Views/StartupView.axaml` (status bar section, lines 55-71)

**What to do:**

In the `<Border DockPanel.Dock="Bottom"` status bar, convert the inner `<StackPanel>` to a `<DockPanel>` and add a right-aligned error `<TextBlock>` — exactly like `ProcessesView.axaml` does it.

Replace the status bar content (lines 56-71 in StartupView.axaml):

```xml
<!-- Status bar -->
<Border DockPanel.Dock="Bottom"
        Padding="16,6"
        Background="{StaticResource BgSecondaryBrush}"
        BorderBrush="{StaticResource BorderSubtleBrush}"
        BorderThickness="0,1,0,0">
  <DockPanel>

    <!-- Error message — right-aligned, hidden when empty -->
    <TextBlock DockPanel.Dock="Right"
               Text="{Binding LastError}"
               IsVisible="{Binding LastError, Converter={x:Static StringConverters.IsNotNullOrEmpty}}"
               FontSize="11"
               Foreground="{StaticResource ColorDangerBrush}"
               VerticalAlignment="Center"/>

    <StackPanel Orientation="Horizontal" Spacing="24">
      <TextBlock FontSize="11" Foreground="{StaticResource TextSecondaryBrush}">
        <Run Text="Total: "/>
        <Run Text="{Binding TotalCount}"   Foreground="{StaticResource TextPrimaryBrush}"  FontWeight="Medium"/>
      </TextBlock>
      <TextBlock FontSize="11" Foreground="{StaticResource TextSecondaryBrush}">
        <Run Text="Enabled: "/>
        <Run Text="{Binding EnabledCount}" Foreground="{StaticResource ColorSuccessBrush}" FontWeight="Medium"/>
      </TextBlock>
    </StackPanel>

  </DockPanel>
</Border>
```

**Verify:** `dotnet build NexusMonitor.sln` → 0 errors. Visually: LastError appears when toggle fails (e.g. no admin rights).

**Commit:**
```
git add src/NexusMonitor.UI/Views/StartupView.axaml
git commit -m "fix: show LastError in StartupView status bar"
```

---

## Task 2: Network view — TCP connection state color badges

**Goal:** Replace the plain-text `State` column with color-coded badge cells identical to the Services tab's status badges. States: ESTABLISHED=green, LISTEN=blue, TIME_WAIT/CLOSE_WAIT/FIN_WAIT=orange, CLOSED/UNKNOWN=gray.

**Files:**
- Modify: `src/NexusMonitor.UI/Converters/ServiceConverters.cs` — add two new converters
- Modify: `src/NexusMonitor.UI/Views/NetworkView.axaml` — replace State text column with template column

**Step 1: Add converters to ServiceConverters.cs**

Add at the bottom of `src/NexusMonitor.UI/Converters/ServiceConverters.cs`:

```csharp
/// <summary>Maps TcpConnectionState to a background brush for the badge.</summary>
public class TcpStateBrushConverter : IValueConverter
{
    public static readonly TcpStateBrushConverter Instance = new();
    private static readonly SolidColorBrush _green  = new(Color.Parse("#34C759"));
    private static readonly SolidColorBrush _blue   = new(Color.Parse("#0A84FF"));
    private static readonly SolidColorBrush _orange = new(Color.Parse("#FF9F0A"));
    private static readonly SolidColorBrush _gray   = new(Color.Parse("#636366"));

    public object? Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is TcpConnectionState state ? state switch
        {
            TcpConnectionState.Established                                      => _green,
            TcpConnectionState.Listen                                           => _blue,
            TcpConnectionState.TimeWait or TcpConnectionState.CloseWait
                or TcpConnectionState.FinWait1 or TcpConnectionState.FinWait2
                or TcpConnectionState.Closing or TcpConnectionState.LastAck    => _orange,
            _                                                                   => _gray,
        } : _gray;

    public object? ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => AvaloniaProperty.UnsetValue;
}

/// <summary>Maps TcpConnectionState to a short display label.</summary>
public class TcpStateLabelConverter : IValueConverter
{
    public static readonly TcpStateLabelConverter Instance = new();

    public object? Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is TcpConnectionState state ? state switch
        {
            TcpConnectionState.Established  => "ESTABLISHED",
            TcpConnectionState.Listen       => "LISTEN",
            TcpConnectionState.SynSent      => "SYN_SENT",
            TcpConnectionState.SynReceived  => "SYN_RCVD",
            TcpConnectionState.FinWait1     => "FIN_WAIT1",
            TcpConnectionState.FinWait2     => "FIN_WAIT2",
            TcpConnectionState.CloseWait    => "CLOSE_WAIT",
            TcpConnectionState.Closing      => "CLOSING",
            TcpConnectionState.LastAck      => "LAST_ACK",
            TcpConnectionState.TimeWait     => "TIME_WAIT",
            TcpConnectionState.Closed       => "CLOSED",
            TcpConnectionState.DeleteTcb    => "DELETE_TCB",
            _                               => "—",
        } : "—";

    public object? ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => AvaloniaProperty.UnsetValue;
}
```

Make sure to add `using NexusMonitor.Core.Models;` if not already present, and `using Avalonia;` (for `AvaloniaProperty`).

**Step 2: Modify NetworkView.axaml**

Add the two converters to `<UserControl.Resources>`:
```xml
<UserControl.Resources>
  <conv:TcpStateBrushConverter x:Key="TcpStateBrush"/>
  <conv:TcpStateLabelConverter x:Key="TcpStateLabel"/>
</UserControl.Resources>
```

Replace the plain State text column:
```xml
<!-- OLD: -->
<DataGridTextColumn Header="State" Binding="{Binding State}" Width="120" IsReadOnly="True"/>

<!-- NEW: -->
<DataGridTemplateColumn Header="State" Width="130" SortMemberPath="State">
  <DataGridTemplateColumn.CellTemplate>
    <DataTemplate>
      <Border CornerRadius="4"
              Padding="6,2"
              Margin="4,3"
              HorizontalAlignment="Left"
              Background="{Binding State, Converter={StaticResource TcpStateBrush}}">
        <TextBlock Text="{Binding State, Converter={StaticResource TcpStateLabel}}"
                   FontSize="10" FontWeight="Medium"
                   Foreground="White"/>
      </Border>
    </DataTemplate>
  </DataGridTemplateColumn.CellTemplate>
</DataGridTemplateColumn>
```

Also add `xmlns:conv="using:NexusMonitor.UI.Converters"` to the UserControl root element if not already present. (Check — NetworkView.axaml does NOT currently have it.)

**Verify:** Build passes. Network tab shows colored badges instead of plain "Established" text.

**Commit:**
```
git add src/NexusMonitor.UI/Converters/ServiceConverters.cs src/NexusMonitor.UI/Views/NetworkView.axaml
git commit -m "feat: TCP state color badges in Network view"
```

---

## Task 3: Settings — Real theme toggle + JSON persistence

**Goal:** Make the Settings tab functional. Theme (dark/light) toggles immediately via Avalonia `RequestedThemeVariant`. Settings persist to `%APPDATA%\NexusMonitor\settings.json` and are restored on next launch.

**Files:**
- Create: `src/NexusMonitor.Core/Models/AppSettings.cs`
- Create: `src/NexusMonitor.Core/Services/SettingsService.cs`
- Modify: `src/NexusMonitor.UI/ViewModels/SettingsViewModel.cs`
- Modify: `src/NexusMonitor.UI/Views/SettingsView.axaml`
- Modify: `src/NexusMonitor.UI/App.axaml.cs` — register `SettingsService` as singleton, apply saved theme on startup

**Step 1: Create AppSettings.cs**

```csharp
// src/NexusMonitor.Core/Models/AppSettings.cs
namespace NexusMonitor.Core.Models;

public class AppSettings
{
    public bool IsDarkTheme { get; set; } = true;
}
```

**Step 2: Create SettingsService.cs**

```csharp
// src/NexusMonitor.Core/Services/SettingsService.cs
using System.Text.Json;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Services;

public class SettingsService
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NexusMonitor", "settings.json");

    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    public AppSettings Current { get; private set; } = new();

    public SettingsService() => Load();

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
        }
        catch { Current = new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, _opts));
        }
        catch { }
    }
}
```

**Step 3: Update SettingsViewModel.cs**

```csharp
// src/NexusMonitor.UI/ViewModels/SettingsViewModel.cs
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using NexusMonitor.Core.Services;

namespace NexusMonitor.UI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settings;

    [ObservableProperty] private bool _isDarkTheme;

    public SettingsViewModel(SettingsService settings)
    {
        Title      = "Settings";
        _settings  = settings;
        _isDarkTheme = settings.Current.IsDarkTheme;
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        // Apply immediately
        if (Application.Current is not null)
            Application.Current.RequestedThemeVariant =
                value ? ThemeVariant.Dark : ThemeVariant.Light;

        // Persist
        _settings.Current.IsDarkTheme = value;
        _settings.Save();
    }
}
```

**Step 4: Update SettingsView.axaml**

Replace the hardcoded theme toggles with data-bound ones:
```xml
<Grid ColumnDefinitions="*,Auto">
  <TextBlock Grid.Column="0" Text="Theme" FontSize="13"
             Foreground="{StaticResource TextSecondaryBrush}"
             VerticalAlignment="Center"/>
  <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="4">
    <ToggleButton Classes="nx-toggle"
                  Content="Dark"
                  IsChecked="{Binding IsDarkTheme}"/>
    <ToggleButton Classes="nx-toggle"
                  Content="Light"
                  IsChecked="{Binding IsDarkTheme, Converter={x:Static BoolConverters.Not}}"/>
  </StackPanel>
</Grid>
```

Also update the version line: `Version 0.1.0 — Phase 3 (Polish & Settings)`

**Step 5: Register SettingsService and apply theme in App.axaml.cs**

In `BuildServices()`, add before ViewModels:
```csharp
services.AddSingleton<SettingsService>();
```

Change `SettingsViewModel` registration to inject it:
```csharp
services.AddSingleton<SettingsViewModel>();
```
(Already AddSingleton — just confirm the constructor will receive `SettingsService` via DI.)

In `OnFrameworkInitializationCompleted()`, after `Services = BuildServices()`, apply the saved theme:
```csharp
var settingsSvc = Services.GetRequiredService<SettingsService>();
RequestedThemeVariant = settingsSvc.Current.IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
```

Wait — `RequestedThemeVariant` is on `Application`, which is `this`. So:
```csharp
var saved = Services.GetRequiredService<SettingsService>();
RequestedThemeVariant = saved.Current.IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
```
Add `using Avalonia.Styling;` to `App.axaml.cs`.

**Verify:** Build passes. Toggle Dark/Light in Settings — entire UI theme changes immediately. Restart app — saved theme is restored.

**Commit:**
```
git add src/NexusMonitor.Core/Models/AppSettings.cs src/NexusMonitor.Core/Services/SettingsService.cs src/NexusMonitor.UI/ViewModels/SettingsViewModel.cs src/NexusMonitor.UI/Views/SettingsView.axaml src/NexusMonitor.UI/App.axaml.cs
git commit -m "feat: functional Settings tab with dark/light theme toggle and JSON persistence"
```

---

## Task 4: Process priority — wire Set Priority submenu

**Goal:** The context menu in `ProcessesView.axaml` already has "Set Priority" submenu items but they are inert (no `Command` binding). Wire them to a `SetPriorityCommand` in `ProcessesViewModel`.

**Files:**
- Modify: `src/NexusMonitor.UI/ViewModels/ProcessesViewModel.cs` — add `SetPriorityCommand`
- Modify: `src/NexusMonitor.UI/Views/ProcessesView.axaml` — bind submenu items

**Step 1: Add SetPriorityCommand to ProcessesViewModel.cs**

The interface already has:
```csharp
Task SetPriorityAsync(int pid, ProcessPriority priority, CancellationToken ct = default);
```

And `ProcessPriority` enum is: `Idle, BelowNormal, Normal, AboveNormal, High, RealTime`.

Add to ProcessesViewModel after the `ResumeProcess` command:

```csharp
[RelayCommand]
private async Task SetPriority(string priorityName)
{
    if (SelectedProcess is null) return;
    if (!Enum.TryParse<ProcessPriority>(priorityName, out var priority)) return;
    try
    {
        LastError = string.Empty;
        await _processProvider.SetPriorityAsync(SelectedProcess.Pid, priority, _cts.Token);
    }
    catch (Exception ex) { LastError = $"Set priority failed: {ex.Message}"; }
}
```

`[RelayCommand]` generates `SetPriorityCommand` which accepts an `object?` parameter (passed as `CommandParameter`).

**Step 2: Wire submenu in ProcessesView.axaml**

Replace the "Set Priority" submenu:
```xml
<MenuItem Header="Set Priority">
  <MenuItem Header="Idle"         Command="{Binding SetPriorityCommand}" CommandParameter="Idle"/>
  <MenuItem Header="Below Normal" Command="{Binding SetPriorityCommand}" CommandParameter="BelowNormal"/>
  <MenuItem Header="Normal"       Command="{Binding SetPriorityCommand}" CommandParameter="Normal"/>
  <MenuItem Header="Above Normal" Command="{Binding SetPriorityCommand}" CommandParameter="AboveNormal"/>
  <MenuItem Header="High"         Command="{Binding SetPriorityCommand}" CommandParameter="High"/>
  <MenuItem Header="Real Time"    Command="{Binding SetPriorityCommand}" CommandParameter="RealTime"/>
</MenuItem>
```

**Verify:** Build passes. Right-click a process → Set Priority → Normal → no error in status bar. Try "RealTime" on a system process → `LastError` shows the failure.

**Commit:**
```
git add src/NexusMonitor.UI/ViewModels/ProcessesViewModel.cs src/NexusMonitor.UI/Views/ProcessesView.axaml
git commit -m "feat: wire Set Priority submenu to SetPriorityCommand in ProcessesView"
```

---

## Task 5: Performance view — per-drive disk usage breakdown

**Goal:** Below the Disk I/O graph, add a "Disk Drives" panel listing each fixed drive with its capacity bar, free space, and drive letter — similar to Windows Task Manager's "Disk" tab summary.

**Files:**
- Modify: `src/NexusMonitor.UI/ViewModels/PerformanceViewModel.cs` — add `DriveRows` observable list
- Create: (no new file — add `DriveRowViewModel` record at bottom of PerformanceViewModel.cs)
- Modify: `src/NexusMonitor.UI/Views/PerformanceView.axaml` — add drives panel after disk I/O section

**Step 1: Add DriveRowViewModel record and DriveRows property**

At the bottom of PerformanceViewModel.cs (above or beside `CoreCellViewModel`), add:
```csharp
public record DriveRowViewModel(
    string DriveLetter,
    string Label,
    double TotalGb,
    double UsedGb,
    double FreeGb,
    double UsedPercent);
```

In `PerformanceViewModel`, add the field and property:
```csharp
[ObservableProperty] private IReadOnlyList<DriveRowViewModel> _driveRows = [];
```

In the `Update()` method, after updating disk I/O counters, add:
```csharp
// Per-drive summary
if (m.Disks.Count > 0)
    DriveRows = m.Disks
        .Where(d => d.TotalBytes > 0)
        .Select(d =>
        {
            double totalGb = Math.Round(d.TotalBytes / 1e9, 1);
            double freeGb  = Math.Round(d.FreeBytes  / 1e9, 1);
            double usedGb  = Math.Round(totalGb - freeGb, 1);
            double pct     = totalGb > 0 ? Math.Round((usedGb / totalGb) * 100, 0) : 0;
            return new DriveRowViewModel(d.DriveLetter, d.Label, totalGb, usedGb, freeGb, pct);
        })
        .ToList();
```

**Step 2: Add drives panel to PerformanceView.axaml**

Add after the closing `</Border>` of the Disk I/O card (before the Network card in the Grid), insert a new standalone panel **between** the disk+network Grid row and the end of the outer StackPanel. Actually — add it after the `<Grid ColumnDefinitions="*,12,*">` disk+network block:

```xml
<!-- ===== DISK DRIVES ===== -->
<Border Margin="0,0,0,12"
        Padding="16"
        Background="{StaticResource BgElevatedBrush}"
        BorderBrush="{StaticResource GlassBorderBrush}"
        BorderThickness="1"
        CornerRadius="{StaticResource CornerLG}">
  <StackPanel Spacing="8">
    <TextBlock Text="Disk Drives"
               FontSize="13" FontWeight="Medium"
               Foreground="{StaticResource TextPrimaryBrush}"/>
    <ItemsControl ItemsSource="{Binding DriveRows}">
      <ItemsControl.ItemTemplate>
        <DataTemplate x:DataType="vm:DriveRowViewModel">
          <Grid ColumnDefinitions="48,*,120" Margin="0,4,0,0" RowDefinitions="Auto,4">
            <!-- Drive letter -->
            <TextBlock Grid.Column="0" Grid.Row="0"
                       Text="{Binding DriveLetter}"
                       FontSize="13" FontWeight="SemiBold"
                       Foreground="{StaticResource TextPrimaryBrush}"
                       VerticalAlignment="Center"/>
            <!-- Capacity bar -->
            <Grid Grid.Column="1" Grid.Row="0" Margin="8,0">
              <Rectangle Height="6" RadiusX="3" RadiusY="3"
                         Fill="{StaticResource BgHoverBrush}"
                         HorizontalAlignment="Stretch"/>
              <Rectangle Height="6" RadiusX="3" RadiusY="3"
                         Fill="{StaticResource AccentBlueBrush}"
                         HorizontalAlignment="Left"
                         Width="{Binding UsedPercent}"/>
            </Grid>
            <!-- Stats -->
            <StackPanel Grid.Column="2" Grid.Row="0" Orientation="Horizontal" Spacing="6" HorizontalAlignment="Right">
              <TextBlock FontSize="11" Foreground="{StaticResource TextSecondaryBrush}">
                <Run Text="{Binding UsedGb, StringFormat='{}{0:F1} GB'}"/>
                <Run Text=" / "/>
                <Run Text="{Binding TotalGb, StringFormat='{}{0:F1} GB'}"/>
              </TextBlock>
              <TextBlock Text="{Binding UsedPercent, StringFormat='({0:F0}%)'}"
                         FontSize="11" Foreground="{StaticResource TextTertiaryBrush}"/>
            </StackPanel>
            <!-- Drive label (second row) -->
            <TextBlock Grid.Column="0" Grid.ColumnSpan="3" Grid.Row="1"
                       Text="{Binding Label}"
                       FontSize="10"
                       Foreground="{StaticResource TextTertiaryBrush}"
                       Margin="0,2,0,0"/>
          </Grid>
        </DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
  </StackPanel>
</Border>
```

**Note on bar width:** The `Width="{Binding UsedPercent}"` trick works in Avalonia for this pattern only when the parent has a fixed or bounded width. In a bounded `Grid Column="1"` with `Width="*"`, this may not scale correctly — if the bar doesn't render right, use a `ProgressBar` instead:
```xml
<ProgressBar Value="{Binding UsedPercent}" Minimum="0" Maximum="100"
             Height="6" Grid.Column="1" Grid.Row="0" Margin="8,0"/>
```
The `ProgressBar` approach is simpler and guaranteed correct. Use it.

**Verify:** Build passes. Performance tab shows a "Disk Drives" section with C: bar filling to ~60% and correct GB values.

**Commit:**
```
git add src/NexusMonitor.UI/ViewModels/PerformanceViewModel.cs src/NexusMonitor.UI/Views/PerformanceView.axaml
git commit -m "feat: per-drive disk usage breakdown panel in PerformanceView"
```

---

## Build Verification

After all tasks:
```bash
dotnet build NexusMonitor.sln
# Expected: Build succeeded. 0 Warning(s). 0 Error(s).
```

## Codebase Quick-Reference for Implementers

**Pattern: Add a converter**
- File: `src/NexusMonitor.UI/Converters/ServiceConverters.cs` or `ProcessCategoryConverters.cs`
- Implement `IValueConverter`, add `using System.Globalization;`, `using Avalonia.Data.Converters;`, `using Avalonia.Media;`
- Register in XAML resources: `<conv:MyConverter x:Key="MyKey"/>`

**Pattern: ObservableProperty**
- `[ObservableProperty] private Type _fieldName;` generates `FieldName` property + `OnFieldNameChanged` partial
- `[NotifyPropertyChangedFor(nameof(OtherProp))]` for computed properties

**Pattern: RelayCommand with parameter**
- `[RelayCommand] private async Task DoThing(string param)` generates `DoThingCommand`
- In XAML: `Command="{Binding DoThingCommand}" CommandParameter="value"`

**DI: All VMs are AddSingleton** — do not change to AddTransient.

**avares:// paths use assembly name `NexusMonitor`**, not namespace `NexusMonitor.UI`.

**Rx threading:** `.ObserveOn(RxApp.MainThreadScheduler)` is already on the subscription — do NOT add inner `Dispatcher.UIThread.Post` in the callback.

**Theme switching:** `Application.Current.RequestedThemeVariant = ThemeVariant.Dark/Light`
- Requires `using Avalonia.Styling;`

**Colors.axaml has:** `AccentBlueBrush`, `ColorSuccessBrush`, `ColorDangerBrush`, `ColorWarningBrush`, `ColorInfoBrush`, `TextPrimaryBrush`, `TextSecondaryBrush`, `TextTertiaryBrush`, `BgElevatedBrush`, `BgSecondaryBrush`, `BgHoverBrush`, `BorderSubtleBrush`, `GlassBorderBrush`.
