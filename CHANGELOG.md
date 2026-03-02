# Changelog

All notable changes to Nexus System Monitor will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-03-01

### Added

#### Core Monitoring
- Real-time CPU metrics: overall usage, per-core breakdown, frequency, temperature (Windows)
- Memory metrics: total, used, available, commit, paged pool
- Disk metrics: usage per volume, read/write throughput, queue depth
- Network metrics: bytes sent/received per adapter, active connections
- GPU metrics: utilization, VRAM usage, temperature (Windows/NVIDIA)
- Process list: CPU, memory, disk I/O, network I/O, priority, affinity, status
- Services manager: start, stop, restart, enable, disable
- Startup items: enable/disable startup programs and services
- Network connections viewer: TCP/UDP connections with PID, state, send/recv throughput

#### System Intelligence
- ProBalance automatic load balancing: CPU priority management under load
- Gaming Mode: auto-optimize foreground game processes
- Rules Engine: define process rules that trigger on events
- Alerts: configurable threshold-based alerts with OS notifications

#### Persistence & Observability
- Metrics persistence: SQLite database with automatic data retention tiers
  - Raw data: 1 hour
  - 1-minute rollups: 7 days
  - 5-minute rollups: 30 days
  - 1-hour rollups: 1 year
- Historical Viewer: charts and event timeline from the metrics database
- Anomaly Detection: sliding-window statistical engine writing anomaly events
- Prometheus metrics exporter: `/metrics` endpoint on configurable port
- Telegraf configuration generator: in-app setup for Telegraf → InfluxDB/Prometheus
- Grafana integration guide: step-by-step setup built into the app

#### UI & Experience
- Avalonia UI 11.2.3 cross-platform desktop application
- Liquid Glass theme with configurable backdrop blur modes
- 8 accent color presets
- Custom title bar with window controls
- System tray icon with quick-access menu
- Desktop overlay widget for at-a-glance metrics
- Disk Analyzer tab with treemap visualization

#### Packaging & Distribution
- Installable packages: Windows setup EXE, macOS DMG, Linux AppImage and .deb
- Portable archives: ZIP (Windows), tar.gz (macOS, Linux) for all 6 RID targets
  - win-x64, win-arm64
  - osx-x64, osx-arm64
  - linux-x64, linux-arm64
- Automated release workflow via GitHub Actions (triggered on `v*` tags)

#### Platform Support
- **Windows 10/11 (x64, ARM64):** Full feature support. Requires administrator privileges.
- **macOS 12+ (Intel + Apple Silicon):** Full support. Unsigned — see README for Gatekeeper bypass.
- **Linux (x64, ARM64):** Full support. Best tested on Ubuntu 22.04+.

[0.1.0]: https://github.com/brass458/nexus-system-monitor/releases/tag/v0.1.0
