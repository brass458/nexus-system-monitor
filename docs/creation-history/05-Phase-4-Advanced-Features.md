# Phase 4: Advanced Features & Settings

**Status**: COMPLETE
**Date**: 2026-02-27

## Overview

Phase 4 implemented process management features, settings system, theme customization, and optimization recommendations.

## Key Achievements

### Process Management ✓

**Priority Control**:
- Set process priority (Idle → RealTime)
- Visual indicators for current priority
- Batch operations on multiple processes
- Windows API: SetPriorityClass

**Affinity Control**:
- CPU core affinity/pinning
- Visual CPU mask selector
- Helpful for NUMA systems
- Windows API: SetProcessAffinityMask

**Process Control**:
- Kill process (force termination)
- Suspend process (freeze threads)
- Resume process (wake threads)
- Kill process tree (children too)

**I/O & Memory Priority**:
- Set I/O priority (VeryLow → High)
- Set memory priority
- Set efficiency mode (EE scheduling on Win11)

### Settings System ✓

**AppSettings Model**:
```csharp
public class AppSettings
{
    public bool IsDarkTheme { get; set; }

    // Theme customization
    public string AccentColorHex { get; set; }
    public double WindowOpacity { get; set; }

    // Feature toggles
    public bool ShowOverlayWidget { get; set; }
    public bool AutoStartMinimized { get; set; }

    // Performance
    public int UpdateIntervalMs { get; set; }

    // Process rules (see Rules Engine)
    public List<ProcessRule> Rules { get; set; }
}
```

**Persistent Storage**:
- JSON-based settings file
- Located in: `%APPDATA%/NexusMonitor/settings.json`
- Auto-load on startup
- Auto-save on changes
- Graceful defaults for missing values

**Settings Service**:
```csharp
public class SettingsService : IDisposable
{
    public AppSettings Current { get; private set; }

    public async Task SaveAsync();
    public async Task LoadAsync();
    public void ApplyTheme();
}
```

### Theme System ✓

**Color Customization**:
- 8 pre-built accent colors
  - Blue (default)
  - Purple
  - Cyan
  - Green
  - Orange
  - Red
  - Pink
  - Neutral Gray

**Runtime Theme Changes**:
- Accent color picker in Settings
- Changes apply immediately to all open windows
- Uses DynamicResource bindings (not static)
- Smooth color transition

**Implementation**:
```csharp
private void SetAccentColor(string hexColor)
{
    var color = Color.Parse(hexColor);
    var brush = new SolidColorBrush(color);

    // Update theme resource
    Application.Current.Resources["AccentBlueBrush"] = brush;

    // All {DynamicResource AccentBlueBrush} update instantly
}
```

### Aero Glass / Liquid Glass ✓

**Glass Effect Settings**:
- Toggle Aero Glass on/off
- Opacity slider (0-100%)
- Backdrop blur modes:
  - Acrylic (default - modern frosted glass)
  - Blur (lightweight)
  - None (flat color)

**Implementation**:
```csharp
private void ApplyGlass(bool enabled, double opacity)
{
    if (enabled)
    {
        window.Background = new AcrylicBrush
        {
            Material = AcrylicMaterial.Regular,
            Opacity = opacity
        };
        window.TransparencyLevelHint = TransparencyLevel.AcrylicBlur;
    }
    else
    {
        window.Background = new SolidColorBrush(Color.Parse("#1E1E1E"));
        window.TransparencyLevelHint = TransparencyLevel.None;
    }
}
```

**Visual Polish**:
- Semi-transparent background (80% opacity default)
- Blurred backdrop effect
- Smooth glass transitions
- macOS-inspired aesthetic

### Optimization Recommendations ✓

**Smart Analysis**:
- Scans running processes
- Identifies resource hogs
- Categorizes by impact level
  - **Critical**: >80% CPU or >4GB RAM
  - **High**: >60% CPU or >2GB RAM
  - **Medium**: >30% CPU or >500MB RAM

**Recommendations Engine**:
```csharp
public enum ImpactLevel { Critical, High, Medium }

public record RecommendationRow(
    int Pid,
    string Name,
    double CpuPercent,
    long WorkingSetBytes,
    ImpactLevel Impact
)
{
    public string Impact => ImpactLevel switch
    {
        ImpactLevel.Critical => "⚠️ Critical",
        ImpactLevel.High => "⚠ High",
        ImpactLevel.Medium => "ℹ Medium",
    };
}
```

**Actions Available**:
- One-click process termination for identified issues
- Set to lower priority to reduce system impact
- Suspend temporarily while you work
- Open process details for investigation
- Add to rules engine for automatic handling

**Safety**:
- Prevents killing critical system processes
- Confirmation dialogs for dangerous operations
- Undo via process restart (if possible)
- Logging of all recommendations and actions

### Process Rules Engine ✓

**Rule Types**:

**Disallowed Processes**:
- Automatically terminates on launch
- Useful for blocking malware/unwanted software
- User-maintained blacklist

**Persistent Actions**:
- Auto-set priority on process launch
- Auto-set CPU affinity
- Auto-set I/O priority
- Auto-set memory priority
- Applied once at startup

**Watchdog Conditions**:
- Monitor CPU usage
- Monitor RAM usage
- Trigger actions when thresholds exceeded
- Configurable duration (must sustain for X seconds)

**Watchdog Actions**:
- Set to Below Normal priority
- Set to Idle priority
- Terminate process

**Rule Matching**:
- Pattern matching on process name
- Regex support for complex patterns
- Can exclude system processes

**Example Rule**:
```json
{
  "name": "Heavy Background Process",
  "pattern": "backup.exe",
  "priority": "BelowNormal",
  "watchdog": {
    "type": "CpuAbove",
    "threshold": 80,
    "durationSeconds": 30,
    "action": "SetBelowNormal"
  }
}
```

### Rules Engine Implementation ✓

**Lifecycle**:
1. Start on app launch
2. Subscribe to process stream
3. On new process: apply persistent rules
4. Every tick: evaluate watchdog conditions
5. Track condition state (first-seen time)
6. Fire action when sustained long enough
7. Clean up exited processes

**Concurrency**:
- Subscribes to process observable
- All work on UI thread (safe)
- Async actions don't block monitoring
- Graceful error handling

**Persistence**:
- Rules saved in AppSettings.Rules
- Auto-load on startup
- Can be managed in Settings UI

### Settings UI ✓

**Settings Panel Layout**:
```
┌─────────────────────────────────────┐
│  Settings                           │
├─────────────────────────────────────┤
│ 🎨 Appearance                       │
│   Dark Theme:    [Toggle]           │
│   Accent Color:  [Color Picker]     │
│   Glass Effect:  [Enabled]          │
│   Glass Opacity: [======] 80%       │
│                                    │
│ 📊 Performance                      │
│   Update Interval: [2000] ms        │
│   Show Overlay Widget: [Toggle]     │
│                                    │
│ 🛡️ Process Rules                    │
│   [+ Add Rule]                      │
│   [Edit / Delete buttons]           │
│                                    │
│ ℹ️ About                            │
│   Version: 1.0.0                   │
│   License: MIT                     │
└─────────────────────────────────────┘
```

## Architecture

### Dependency Injection Setup

```csharp
services.AddSingleton<SettingsService>();
services.AddSingleton<RulesEngine>();
services.AddSingleton<SettingsViewModel>();
services.AddSingleton<OptimizationViewModel>();
```

### Command Pattern

```csharp
public partial class ProcessesViewModel : ViewModelBase
{
    [RelayCommand]
    private async Task KillProcess()
    {
        if (SelectedProcess is null) return;
        await _processProvider.KillProcessAsync(SelectedProcess.Pid);
    }

    [RelayCommand]
    private async Task SetPriority(ProcessPriority priority)
    {
        await _processProvider.SetPriorityAsync(SelectedProcess.Pid, priority);
    }
}
```

## Performance Optimizations

- Settings cache (not reloading from disk on every access)
- Debounced settings saves (batch writes)
- Lazy theme initialization
- Rules engine only evaluates enabled rules
- Efficient pattern matching (compiled regex cache)

## Known Limitations

- Process rules stored in JSON (not database)
- No rule scheduling/time-based rules yet
- Glass effect quality depends on Windows Composition capabilities
- Some operations require admin privileges

## Files Modified/Created

```
src/NexusMonitor.Core/
├── Models/AppSettings.cs (NEW)
├── Models/ProcessRule.cs (NEW)
├── Models/RecommendationRow.cs (NEW)
├── Services/SettingsService.cs (NEW)
└── Rules/RulesEngine.cs (NEW)

src/NexusMonitor.UI/
├── ViewModels/SettingsViewModel.cs (NEW)
├── ViewModels/OptimizationViewModel.cs (NEW)
├── Views/SettingsView.axaml (NEW)
├── Views/OptimizationView.axaml (NEW)
└── Views/MainWindow.axaml (UPDATED - theme resources)

src/NexusMonitor.Platform.Windows/
└── WindowsProcessProvider.cs (UPDATED - management commands)
```

## Next Steps

Phase 5 will add:
- Full thread and environment variable UI
- macOS provider scaffolding
- Linux provider basics
- Startup items management
- Better error messages
- UX polish

See: [[06-Phase-5-Extensions]]
