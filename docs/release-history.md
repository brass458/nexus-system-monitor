---
type: note
date: 2026-03-05
project: NexusSystemMonitor
tags: [nexus, release-history, changelog]
---

# Release History

Narrative mapping of development phases to public releases.

---

## Phase-to-Release Map

| Phase(s) | Version | Date | Theme |
|----------|---------|------|-------|
| 1–6 | pre-release | 2026-02-26–28 | Foundation, Windows APIs, Cross-platform scaffold |
| 7–10 | pre-release | 2026-02-28 | iOS 26 glass theme, System Info, sidebar drag, active NIC |
| 11 | v0.1.0 (partial) | 2026-03-01 | Metrics persistence (SQLite) |
| 12 | v0.1.0 (partial) | 2026-03-01 | Historical Viewer |
| 13 | v0.1.0 (partial) | 2026-03-01 | Anomaly Detection |
| 14–16 + CI | v0.1.0 | 2026-03-01 | Prometheus, Telegraf, Grafana, first public release |
| Performance sweep | v0.1.1 | 2026-03-03 | Linux perf, memory leaks, theme switching |
| Post-v0.1.1 | v0.1.2 | 2026-03-03 | System theme, Crystal Glass rename |
| LAN scanner | v0.1.3 | 2026-03-03 | Nmap integration, Linux init, font scaling |
| Color picker / sort fixes | v0.1.3.1 | 2026-03-04 | Post-release fixes, shared color picker |
| Sort persistence | v0.1.4 | 2026-03-04 | DataGrid sort survives tab switches |
| Dashboard | v0.1.5 | 2026-03-04 | System health, bottleneck detection |
| Bottleneck events | v0.1.5.1 | 2026-03-04 | Resource incidents in History tab |
| Swatch palettes | v0.1.6 | 2026-03-05 | Dynamic per-preset surface palettes |

---

## Pre-Release: Phases 1–10 (2026-02-26 to 2026-02-28)

**What was built:** The complete foundation of Nexus System Monitor.

Phases 1–6 established the solution architecture: Core abstractions (`IProcessProvider`, `ISystemMetricsProvider`, etc.), Windows P/Invoke implementations (PDH counters, ToolHelp32, NtDll), the Avalonia MVVM shell with all major tabs, and cross-platform scaffolds for macOS and Linux. Phase 6 added the Task Manager-style device sidebar, context menus across all tabs, an appearance settings panel with 8 accent presets, and the desktop overlay widget.

Phases 7–10 (not individually documented) completed: the iOS 26 Liquid Glass aesthetic, the System Information tab with Apple-style hardware layout, sidebar drag-reorder, and active NIC throughput detection.

> [!note] Session logs
> See [[creation-history/README|Creation History Archive]] for Phase 1–6 original design docs.

**Key decisions:**
- MVVM with CommunityToolkit.Mvvm (not pure ReactiveUI) for clean `[RelayCommand]` ergonomics
- `WeakReferenceMessenger` for cross-tab navigation (Services → Processes, etc.)
- `ObserveOn(RxApp.MainThreadScheduler)` on all Rx subscriptions
- Platform code isolated behind 5 interfaces — adding a new platform touches only those implementations

---

## v0.1.0 — First Public Release (2026-03-01)

**Theme:** Complete system monitor with observability pipeline.

The public v0.1.0 release bundled the pre-release foundation with three new phases of observability work (Phases 11–16):

- **Phase 11:** SQLite metrics persistence — WAL mode, tiered retention (raw 1h → 1m rollups 7d → 5m rollups 30d → 1h rollups 1yr), top-15 process snapshots per tick, batch writes buffering 30 ticks
- **Phase 12:** Historical Viewer tab — charts and event timeline browsable from the database
- **Phase 13:** Anomaly Detection — sliding-window statistics, `SlidingStats`, `IEventWriter`, anomaly events persisted to SQLite
- **Phase 14:** Prometheus `/metrics` endpoint via `PrometheusExporter`
- **Phase 15:** Telegraf configuration generator in Settings UI
- **Phase 16:** Grafana dashboard template + in-app setup guide

Also included: release packaging (GitHub Actions fan-out CI, 12 artifacts across 6 RIDs, Inno Setup/DMG/AppImage/.deb), custom icon assets, and the full cross-platform implementation (macOS sysctl/Mach/ObjC, Linux procfs/sysfs/multi-init).

> [!note] Session logs
> - [[../../CC-Session-Logs/2026-02-28_07-27_nexus-phase11-plan-repo-sync|Phase 11 Plan & Repo Sync]]
> - [[../../CC-Session-Logs/2026-02-28_09-47_macos-linux-platform-parity|macOS/Linux Platform Parity]]
> - [[../../CC-Session-Logs/2026-02-28_19-14_column-headers-transparency-fix|Column Headers & Transparency Fix]]
> - [[../../CC-Session-Logs/2026-03-01_phase11-status-permissions-fix|Phase 11 Status & Permissions Fix]]
> - [[../../CC-Session-Logs/2026-03-01_10-57_phase-13-anomaly-detection|Phase 13 Anomaly Detection]]
> - [[../../CC-Session-Logs/2026-03-01_20-02_network-estats-auto-hide|Network EStats Auto-Hide]]
> - [[../../CC-Session-Logs/2026-03-02_02-18_nexus-release-packaging|Release Packaging]]

---

## v0.1.1 — Performance & Theme Sweep (2026-03-03)

**Theme:** Correctness, not new features.

The Linux feedback from v0.1.0 revealed significant performance issues. This release fixed 4 memory leaks (SettingsViewModel, SettingsService, FindWindowOverlay, AnomalyDetectionService), reduced Linux CPU usage from ~25% to ~3–8%, reduced Linux RAM from ~300 MB to ~100–150 MB, eliminated a stream-interval race affecting all 9 data providers, and converted 382 `{StaticResource}` → `{DynamicResource}` references to make the Dark↔Light toggle work instantly.

macOS now uses sysctl/statvfs P/Invoke instead of spawning subprocesses for disk I/O.

> [!note] Session log
> [[../../CC-Session-Logs/2026-03-02_05-23_memory-leaks-theme-fixes|Memory Leaks & Theme Fixes]]
> [[../../CC-Session-Logs/2026-03-02_13-50_icon-audit-about-dialog|Icon Audit & About Dialog]]

---

## v0.1.2 — System Theme & Crystal Glass (2026-03-03)

**Theme:** OS integration and visual refinement.

Added a "System" theme option that follows OS dark/light preference at runtime. Renamed "Liquid Glass" to "Crystal Glass" throughout. Made the glass effect opt-in (was previously always-on). Fixed the close dialog's wallpaper bleed-through and light mode text readability.

---

## v0.1.3 — LAN Scanner & Linux Depth (2026-03-03)

**Theme:** New capabilities and platform depth.

Added the Nmap LAN Scanner tab — the most complex single feature in the codebase: async scan orchestration, XML parsing, progress from stderr, OS detection display, host tree with detail sidebar. Also added Linux hardware info (sysfs), Linux temperature scanning, font size multiplier, and text accent color. Tightened defaults (metrics off by default, anomaly detection off by default).

---

## v0.1.3.1 — Post-Release Fixes (2026-03-04)

**Theme:** Polish pass on the new features from v0.1.3.

Fixed: nmap latency (was using jitter `rttvar` instead of RTT `srtt`), color wheel TwoWay binding, sort feedback loop re-entrancy, nmap stderr surfacing, nmap install detection. Consolidated 5 separate color picker windows into one shared `ColorPickerWindow` with `ColorPickerTarget` enum routing.

> [!note] Session log
> [[../../CC-Session-Logs/2026-03-04_04-07_color-picker-nmap-v0131|Color Picker + Nmap + v0.1.3.1]]

---

## v0.1.4 — Sort Persistence (2026-03-04)

**Theme:** One focused bug fix, done properly.

DataGrid sort state (column + direction) now survives tab switches in all four sortable views. The root cause was a subtle async timing issue: `col.Sort()` posts `ProcessSort` via `Dispatcher.UIThread.Post`, so a `finally { _restoringSort = false }` block that ran synchronously fired before the sort callbacks. Fixed by resetting the guard via `Post` (FIFO at same priority) so it drops only after all sort events complete.

---

## v0.1.5 — System Health Dashboard (2026-03-04)

**Theme:** Actionable at-a-glance intelligence.

Added the System Health Dashboard as the new default landing tab: composite health score (0–100), 4 subsystem cards, top-5 process consumers, and bottleneck detection for Gaming/Streaming/VideoEditing/3DRendering/CAD workloads. Reorganised the sidebar into 4 named groups (Pinned/Monitor/Tools/System) with visual separators and drag-constraints. Added Fluent System Icons font.

> [!note] Session log
> [[../../CC-Session-Logs/2026-03-04_20-48_sidebar-grouped-nav-icons|Sidebar Grouped Nav & Icons]]

---

## v0.1.5.1 — Resource Incidents (2026-03-04)

**Theme:** Surface anomaly events in the History tab.

Added `ResourceEvent` model, `EventMonitorService`, and `EventRepository` — discrete threshold-crossing events persisted to a new `resource_events` SQLite table and surfaced as a "Resource Incidents" section in the History tab. Also added GitHub Sponsors configuration.

---

## v0.1.6 — Dynamic Surface Swatch Palettes (2026-03-05)

**Theme:** Theme system completes.

The Settings "Background & Surface Colors" section now shows 8 curated swatches for each of 3 UI surfaces (Window Chrome, Cards & Content, Sidebar & Navigation) that update automatically when switching presets. All 18 presets have hand-curated dark palettes. Added `SwatchColor` record and `SurfaceSwatchPalettes` static class.

> [!note] Session log
> [[../../CC-Session-Logs/2026-03-05_22-36_dynamic-surface-swatch-palettes|Dynamic Surface Swatch Palettes]]
