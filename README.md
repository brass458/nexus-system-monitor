# Nexus System Monitor

**One tool. Every platform. Complete system visibility.**

Nexus System Monitor is a cross-platform desktop application that gives you deep, granular insight into your system's processes, performance, services, and hardware — with a single, consistent interface whether you're on Windows, macOS, or Linux.

---

## Philosophy

Every operating system ships its own task manager, and power users inevitably turn to a patchwork of third-party tools — Process Lasso on Windows, Activity Monitor on macOS, htop on Linux, System Informer for deep inspection, and more. Each one has a different interface, different capabilities, and different learning curves.

Nexus System Monitor exists to end that fragmentation.

The goal is a **synonymous user experience across every desktop platform**. The same layout, the same depth of detail, the same workflows — regardless of whether you're sitting in front of a Windows workstation, a MacBook, or a Linux desktop. You learn the tool once, and it works everywhere.

This isn't a lowest-common-denominator approach. Nexus aims for the **union** of features found in the best system tools available today — the real-time metrics of Windows Task Manager, the process control of Process Lasso, the deep inspection of System Informer, and the hardware detail of system profilers — unified under a modern, visually consistent UI inspired by iOS 26's Liquid Glass design language.

---

## Features

### Real-Time Performance Monitoring
- **CPU** — Overall utilization, per-core breakdown, frequency, temperature, cache info
- **Memory** — Physical and paged usage, speed, slot configuration, committed bytes
- **Disk** — Per-drive I/O rates, read/write throughput, queue depth, capacity and usage (NVMe/SSD/HDD detection)
- **Network** — Per-adapter throughput, active NIC detection, IPv4/IPv6 addresses, link speed
- **GPU** — Utilization, dedicated/shared memory, temperature, per-engine breakdown (3D, Copy, Video Decode/Encode)
- Task Manager-style sidebar with device selection and sparkline history charts

### Process Management
- Full process list with real-time CPU, memory, disk I/O, network, and GPU usage per process
- Color-coded process categories (system, service, user app, .NET managed, GPU-accelerated, suspicious, suspended)
- Set process priority (Idle through RealTime) and CPU affinity
- Set I/O priority, memory priority, and efficiency mode
- Kill, suspend, and resume processes
- Kill entire process trees
- Process dumps for debugging
- Module (DLL) enumeration with version info
- Thread enumeration with per-thread CPU times
- Environment variable inspection
- Open file location, search online, copy paths

### System Information
- Detailed hardware inventory — CPU model, cores, cache hierarchy, virtualization support
- Memory modules with speed, type, and slot info
- GPU details and driver versions
- Disk hardware identification
- Network adapter configuration
- Apple-style clean layout for at-a-glance system specs

### Services Manager
- Enumerate all system services with status and startup type
- Start, stop, and restart services
- Navigate directly to a service's host process

### Startup Items
- View and manage programs that launch at startup
- Enable and disable startup entries
- Open file locations and registry keys

### Network Connections
- Active TCP/UDP connections with state badges
- Local and remote endpoint details
- Navigate to the owning process

### Disk Analyzer
- Visual disk space breakdown by folder
- Multi-threaded directory scanning
- Identify space-hogging directories at a glance

### Alerts & Rules Engine
- Define performance alerts with CPU/RAM thresholds
- Create automation rules — automatically set priority, affinity, or terminate processes when conditions are met
- Watchdog monitoring with configurable duration triggers

### Gaming Mode
- One-click optimization profile for gaming sessions
- Suppress background process interference
- Prioritize game processes automatically

### ProBalance
- Automatic load balancing across running processes
- Prevent any single process from monopolizing system resources

### Optimization Recommendations
- Smart analysis of running processes
- Tiered impact ratings (Critical, High, Medium)
- One-click actions to resolve resource hogs

### Desktop Overlay Widget
- Floating always-on-top widget showing CPU, RAM, network, and GPU at a glance
- Draggable, transparent, and minimal (230 x 168 px)
- Toggle on/off from settings

### Appearance & Theming
- Dark glass theme inspired by iOS 26 Liquid Glass
- 8 accent color presets with runtime switching
- Aero Glass toggle with adjustable opacity
- All theme changes apply instantly across the entire UI

---

## Platform Support

| Platform | Status | Detail Level |
|----------|--------|-------------|
| **Windows** | Full implementation | Complete — P/Invoke, PDH counters, WMI, Win32 APIs |
| **macOS** | Scaffold (in progress) | Provider interfaces implemented, native calls pending |
| **Linux** | Scaffold (in progress) | Provider interfaces implemented, /proc + /sys parsing pending |

The core application, UI, and all business logic are fully cross-platform today. Platform-specific system calls (process enumeration, metrics collection, service management) are isolated behind clean abstraction interfaces, with Windows fully implemented and macOS/Linux providers ready for native API integration.

---

## Running Nexus System Monitor

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) (or later)

### From Source

Clone the repository and build:

```bash
git clone https://github.com/brass458/nexus-system-monitor.git
cd nexus-system-monitor
dotnet build NexusMonitor.sln
```

#### Windows

```bash
dotnet run --project src/NexusMonitor.UI/NexusMonitor.UI.csproj --framework net8.0-windows10.0.17763.0
```

#### macOS

```bash
dotnet run --project src/NexusMonitor.UI/NexusMonitor.UI.csproj --framework net8.0-macos
```

> **Note:** macOS providers are scaffolded but not yet fully implemented. The application will launch with limited system data until native macOS API integration is complete.

#### Linux

```bash
dotnet run --project src/NexusMonitor.UI/NexusMonitor.UI.csproj --framework net8.0
```

> **Note:** Linux providers are scaffolded but not yet fully implemented. The application will launch with limited system data until /proc and /sys parsing is complete.

### Pre-Built Releases

Pre-compiled binaries for Windows, macOS, and Linux are not yet available. They will be published under [Releases](https://github.com/brass458/nexus-system-monitor/releases) once platform implementations are finalized.

---

## Tech Stack

| Component | Technology |
|-----------|-----------|
| Language | C# 12 / .NET 8 |
| UI Framework | [Avalonia UI](https://avaloniaui.net/) 11.2.3 |
| MVVM | [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) 8.3.2 |
| Reactive | [ReactiveUI](https://www.reactiveui.net/) + System.Reactive 6.0.1 |
| Charts | [LiveChartsCore](https://livecharts.dev/) 2.0.0-rc4 (SkiaSharp) |
| Graphics | SkiaSharp 2.88.9 |
| DI | Microsoft.Extensions.DependencyInjection 8.0.1 |

---

## Architecture

```
NexusMonitor.sln
├── src/
│   ├── NexusMonitor.Core/              # Abstractions, models, services (net8.0)
│   ├── NexusMonitor.UI/                # Avalonia desktop app (platform-conditional)
│   ├── NexusMonitor.DiskAnalyzer/      # Disk analysis engine
│   ├── NexusMonitor.Platform.Windows/  # Windows API implementations
│   ├── NexusMonitor.Platform.MacOS/    # macOS implementations
│   └── NexusMonitor.Platform.Linux/    # Linux implementations
└── tests/
    └── NexusMonitor.Core.Tests/        # Unit tests
```

All UI and business logic lives in the platform-agnostic `Core` and `UI` projects. Platform-specific code is isolated behind five core interfaces:

- **ISystemMetricsProvider** — CPU, memory, disk, network, GPU metrics
- **IProcessProvider** — Process enumeration and control
- **INetworkConnectionsProvider** — TCP/UDP connection enumeration
- **IServicesProvider** — System service management
- **IStartupProvider** — Startup item management

Adding support for a new platform means implementing these interfaces against native APIs — the rest of the application works unchanged.

---

## License

[MIT](LICENSE) — Copyright 2026 TheBlackSwordsman
