---
type: note
date: 2026-03-05
project: NexusSystemMonitor
tags: [nexus, architecture, patterns]
---

# Architecture

Current architecture of Nexus System Monitor as of v0.1.6.

---

## Solution Structure

```
Areas/Projects/NexusSystemMonitor/
├── NexusMonitor.sln
├── Directory.Build.props          # Version source of truth: <Version>0.1.6</Version>
├── src/
│   ├── NexusMonitor.Core/         # Abstractions, models, services (net8.0 — fully portable)
│   ├── NexusMonitor.UI/           # Avalonia desktop app (platform-conditional TFM)
│   ├── NexusMonitor.DiskAnalyzer/ # Disk analysis engine (treemap, scanning)
│   ├── NexusMonitor.Platform.Windows/  # Windows API implementations (net8.0-windows)
│   ├── NexusMonitor.Platform.MacOS/    # macOS implementations (net8.0-macos / net8.0 on Windows host)
│   └── NexusMonitor.Platform.Linux/    # Linux implementations (net8.0)
└── tests/
    └── NexusMonitor.Core.Tests/   # Unit tests (mock provider tests)
```

**Assembly name:** `NexusMonitor` (not `NexusMonitor.UI`)
All `avares://` URIs: `avares://NexusMonitor/Assets/...`

---

## Core Interfaces

Five platform-agnostic interfaces isolate all platform-specific code:

| Interface | Responsibility |
|-----------|----------------|
| `ISystemMetricsProvider` | CPU, memory, disk, network, GPU metrics |
| `IProcessProvider` | Process enumeration and control |
| `INetworkConnectionsProvider` | TCP/UDP connection enumeration + EStats |
| `IServicesProvider` | System service management |
| `IStartupProvider` | Startup item management |

Adding a new platform means implementing these 5 interfaces. The rest of the application works unchanged.

---

## Platform Provider Strategy

Each platform project implements the 5 interfaces using native APIs:

**Windows** (`Platform.Windows/`):
- Process metrics: `TotalProcessorTime` delta + `GetProcessIoCounters` (P/Invoke)
- System metrics: PDH counters (`_diskCounters`, `_netCounters`, `_gpuCopyCounters`, etc.)
- Parent PID: `NtQueryInformationProcess`
- Elevation: `GetTokenInformation`
- Services: `EnumServicesStatusExW`
- Modules/Threads: ToolHelp32 (`CreateToolhelp32Snapshot`)
- GPU engines: PDH engine-type counter init via `InitEngineCounters`
- EStats (per-connection throughput): `GetPerTcpConnectionEStats` — errors on TSO/RSC NICs → auto-hides

**macOS** (`Platform.MacOS/`):
- Per-core CPU: `host_processor_info(host, PROCESSOR_CPU_LOAD_INFO=2)` — flat `uint[cpuCount*4]` (user/sys/idle/nice per core); free with `vm_deallocate`
- Disk I/O: `ioreg -c IOBlockStorageDriver -r -k Statistics` → `"Bytes (Read)"/"Bytes (Write)"` per driver
- Foreground window: ObjC P/Invoke into `libobjc.A.dylib` — separate `objc_msgSend_int` signature for `int` returns
- Power plans: `pmset -a lowpowermode` for Power Saver/Balanced/High Performance

**Linux** (`Platform.Linux/`):
- Network PIDs: scan `/proc/[pid]/fd/` symlinks for `socket:[inode]`, cache 2s; column 10 in `/proc/net/tcp` is the inode
- Temperature: scans `/sys/class/hwmon` for `coretemp`/`k10temp`/`zenpower` then `thermal_zone*`
- Init system: detect via `/proc/1/comm` → `/run/systemd/system` → `/run/openrc/softlevel` → `/etc/init.d` fallback
- Init backends: `SystemdBackend`, `SysVinitBackend`, `OpenRcBackend`, `DinitBackend`, `RunitBackend`, `S6Backend`
- Power plans: `powerprofilesctl` first, fallback to `/sys/.../scaling_governor`

**Platform conditional compilation:**
```csharp
#if WINDOWS
    // Windows-only code
#elif MACOS
    // macOS-only code
#elif LINUX
    // Linux-only code
#endif
```
`LINUX` define must appear in both `.csproj` AND all `linux-*.pubxml` publish profiles.

---

## Dependency Injection

All ViewModels registered as `AddSingleton` in `App.axaml.cs`. The DI container auto-disposes `IDisposable` singletons on shutdown — `(Services as IDisposable)?.Dispose()` calls all registered disposables. No manual wiring needed.

`OverlayWindow` is created in `OnFrameworkInitializationCompleted` and wired to `SettingsViewModel.OverlayWindow`.

---

## MVVM + ReactiveUI Patterns

```csharp
// Providers emit observable streams; always marshal to UI thread
_processProvider
  .GetProcessStream(TimeSpan.FromSeconds(2))
  .ObserveOn(RxApp.MainThreadScheduler)    // required
  .Subscribe(processes => UpdateUI(processes));
// Do NOT add inner Dispatcher.UIThread.Post — redundant inside ObserveOn
```

Cross-tab navigation uses `WeakReferenceMessenger`:
```csharp
// Send from ServicesViewModel
WeakReferenceMessenger.Default.Send(new NavigateToProcessMessage(pid));

// Receive in MainViewModel
WeakReferenceMessenger.Default.Register<NavigateToProcessMessage>(this, (r, m) => SwitchToProcessTab(m.Pid));
```

Commands follow CommunityToolkit.Mvvm pattern:
```csharp
[RelayCommand]
private async Task KillProcess() =>
    await _processProvider.KillProcessAsync(SelectedProcess.Pid);
```

---

## Theming System

**Rule:** `{DynamicResource}` for any brush that must update when the theme changes. `{StaticResource}` only for truly static values (corner radii, accent colors that don't change with theme).

382 `StaticResource` → `DynamicResource` replacements were made in v0.1.1 across all 21 AXAML files.

Theme structure in `App.axaml`:
```xml
<ResourceDictionary>
    <ResourceDictionary.ThemeDictionaries>
        <ResourceDictionary x:Key="Dark">  <!-- dark brushes -->
        <ResourceDictionary x:Key="Light"> <!-- light brushes -->
    </ResourceDictionary.ThemeDictionaries>
    <!-- Theme-independent resources (accent colors) -->
</ResourceDictionary>
```

Runtime color changes propagate instantly:
```csharp
Application.Current.Resources["AccentBlueBrush"] = new SolidColorBrush(color);
```

18 theme presets + custom. `SurfaceSwatchPalettes.GetPalette(presetId, isDark)` returns `SwatchColor[8]` per surface. Swatch `Background="{Binding Hex}"` uses Avalonia's string→Brush type converter.

**Fonts:**
- `FluentSystemIcons-Regular.ttf` at `Assets/Fonts/` — registered as `NexusIcons` FontFamily in `Typography.axaml`
- Icon codepoints from `FluentSystemIcons-Regular.json` (key=name, value=decimal codepoint)

---

## Storage Layer

`NexusMonitor.Core/Storage/MetricsStore.cs`:
- SQLite WAL mode via `Microsoft.Data.Sqlite`
- Tiered retention: raw 1h → 1m rollups 7d → 5m rollups 30d → 1h rollups 1yr
- Top-15 process snapshots per tick (by CPU + memory, deduped)
- Batch writes: buffer 30 ticks, flush in single transaction
- `resource_events` table for anomaly/bottleneck events

Estimated steady-state DB size: ~50–100 MB.

---

## Observability Pipeline

```
MetricsStore (SQLite) → HistoricalViewer (in-app)
                      → PrometheusExporter (/metrics endpoint)
                      → Telegraf config generator (Settings UI)
                      → Grafana dashboard (in-app guide)
AnomalyDetectionService → resource_events table → History tab incidents
EventMonitorService     → resource_events table (threshold crossings)
```

---

## Avalonia-Specific Gotchas

| Problem | Solution |
|---------|----------|
| `x:Name` on `DataGridTextColumn` does not generate code-behind field | Iterate `DataGrid.Columns` by header string; put `x:Name` on the `DataGrid` element |
| `col.Sort()` posts async via `Dispatcher.UIThread.Post` | Reset sort guard also via `Post` (FIFO) — not in `finally` |
| `DataGridTextColumn` without explicit `SortMemberPath` returns null in sort handler | Always set `SortMemberPath` explicitly |
| `RelativeSource AncestorType=UserControl` in nested DataTemplate | Requires `x:CompileBindings="False"` |
| `{StaticResource}` does not update on theme change | Must use `{DynamicResource}` for any brush in `ThemeDictionaries` |
| Inline hex colors: only 3, 4, 6, or 8 hex digits valid | Never use 10-digit strings like `#CCE0FFFFFF` — Avalonia crashes with `FormatException` |
| `TransparencyLevelHint` set in XAML causes type conversion error | Set in code-behind only |

---

## Key File Locations

| Area | Path |
|------|------|
| Core models | `src/NexusMonitor.Core/Models/` |
| Core abstractions | `src/NexusMonitor.Core/Abstractions/` |
| Core services | `src/NexusMonitor.Core/Services/` (RulesEngine, SettingsService, AnomalyDetection) |
| Core storage | `src/NexusMonitor.Core/Storage/` (MetricsStore, EventRepository) |
| Core themes | `src/NexusMonitor.Core/Themes/` (SurfaceSwatchPalettes) |
| Core network | `src/NexusMonitor.Core/Network/` (NmapScannerService, NmapXmlParser) |
| Windows native | `src/NexusMonitor.Platform.Windows/Native/` (Kernel32, NtDll, PsApi, AdvApi32) |
| UI ViewModels | `src/NexusMonitor.UI/ViewModels/` |
| UI Views | `src/NexusMonitor.UI/Views/` |
| UI Resources | `src/NexusMonitor.UI/Assets/` + `Styles/` |
| DI setup | `src/NexusMonitor.UI/App.axaml.cs` |
| Version | `Directory.Build.props` |
| CI workflow | `.github/workflows/release.yml` |
| Windows installer | `installer/windows/NexusMonitor.iss` |
