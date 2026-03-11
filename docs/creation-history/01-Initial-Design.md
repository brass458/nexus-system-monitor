# Initial Design & Project Conception

**Date Range**: 2026-02-26 - 2026-02-27

## Project Vision

The user initiated the Nexus Monitor project with the following goals:

> "I want to build a program, much like process lasso, task manager, system informer, and process explorer. All of their features in one application, with a pretty, modern GUI that is close in appearance to the art and design of modern Mac OS X, with aero glass UI features, colored processes, user-friendly UI appearances for simplified and advanced-user task viewing."

### Core Requirements
- **Inspiration**: Combines features from:
  - Process Lasso
  - Windows Task Manager
  - System Informer / Process Hacker
  - Process Explorer

- **Design**: Modern, macOS/iOS 26-inspired with Aero Glass effects
- **Approach**: Unified feature set with beautiful, simplified UI for both simple and advanced users
- **Licensing**: MIT, open-source

## Initial Design Questions

The assistant conducted thorough discovery:
1. Target platforms (Windows, macOS, Linux)
2. UI framework and design approach
3. Feature prioritization
4. Real-time monitoring capabilities
5. Advanced process management features

## Technology Stack Selection

After exploration, the chosen tech stack:

**Language & Framework**:
- C# 12 / .NET 8 (cross-platform capability)
- Avalonia UI 11.2.3 (SkiaSharp-based, cross-platform XAML)

**Dependencies**:
- CommunityToolkit.Mvvm 8.3.2 (MVVM pattern)
- ReactiveUI + System.Reactive 6.0.1 (reactive programming)
- LiveChartsCore.SkiaSharpView.Avalonia 2.0.0-rc4 (charting)
- Microsoft.Extensions.DependencyInjection 8.x (DI container)
- Avalonia.Controls.DataGrid 11.2.3 (data presentation)

## Solution Architecture

```
NexusMonitor.sln
├── src/
│   ├── NexusMonitor.Core/              # Abstractions, models, providers
│   ├── NexusMonitor.Platform.Windows/  # Windows-specific P/Invoke
│   ├── NexusMonitor.Platform.MacOS/    # macOS implementation
│   ├── NexusMonitor.Platform.Linux/    # Linux implementation
│   └── NexusMonitor.UI/                # Avalonia UI application
└── tests/
    └── NexusMonitor.Core.Tests/        # Unit tests
```

## Assembly Configuration

- **Main Assembly Name**: `NexusMonitor` (not NexusMonitor.UI)
- **Resource URI Prefix**: `avares://NexusMonitor/...`
- **Platform TFMs**:
  - Core: `net8.0`
  - Platform.Windows: `net8.0-windows`
  - Platform.MacOS: `net8.0-macos` (on macOS) / `net8.0` (on Windows)
  - Platform.Linux: `net8.0`
  - UI: Platform-conditional

## Key Design Decisions

1. **Cross-Platform First**: All business logic in Core, platform-specific code isolated
2. **Reactive Programming**: Rx for real-time data streams and responsive UI
3. **Dark Theme with Glass**: iOS 26 Liquid Glass effects as primary theme
4. **MVVM Pattern**: Clean separation of concerns, testable ViewModels
5. **Dependency Injection**: Centralized service registration for flexibility
6. **Color-Coded UI**: Visual indicators for process priority, status, resource usage

## Notes

- User has .NET 8 SDK installed
- Project started with clean architecture emphasis
- Early focus on beautiful UI while maintaining feature parity with existing tools
- Cross-platform support from the start (not afterthought)
