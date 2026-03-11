# Phase 5: Extensions & Cross-Platform Scaffold

**Status**: COMPLETE
**Date**: 2026-02-27 to 2026-02-28

## Overview

Phase 5 enhanced the Windows implementation, added thread/environment variable details to the UI, and created cross-platform provider scaffolds for macOS and Linux.

## Key Achievements

### Enhanced Thread & Environment UI ✓

**Thread Details View**:
- Complete thread enumeration in process detail panel
- Thread ID, state, CPU times
- Context switch count
- Thread creation time
- Real-time thread count updates
- Interactive thread details

**Environment Variables Tab**:
- Full environment variable enumeration
- Search/filter by name or value
- Copy variable values
- Binary-safe handling (multiline values)
- Export environment as text file

**Implementation**:
```csharp
// Thread enumeration with state tracking
public record ThreadInfo(
    int ThreadId,
    ThreadState State,
    TimeSpan UserTime,
    TimeSpan KernelTime,
    int ContextSwitches,
    DateTime CreationTime
);

// Environment entry with key-value
public record EnvironmentEntry(
    string Name,
    string Value
);
```

### macOS Provider Scaffold ✓

**MacOSProcessProvider**:
- Interface implementation stub
- Placeholder methods for all operations
- Uses libSystem (macOS system library)
- Ready for implementation

**Platform Detection**:
```csharp
#if MACOS
    services.AddSingleton<IProcessProvider, MacOSProcessProvider>();
    services.AddSingleton<ISystemMetricsProvider, MacOSSystemMetricsProvider>();
#endif
```

**Key APIs (documented for future work)**:
- `getpid()` / `getppid()` - Process IDs
- `proc_listpids()` - Enumerate all processes
- `proc_pidinfo()` - Process information
- `libproc.h` - Process interface functions
- `/proc` filesystem (limited on modern macOS)
- `sysctl()` - System configuration

**Challenges**:
- macOS restricted process access (privacy/security)
- Different memory layout than Windows
- Requires `com.apple.security.get-task-allow` entitlement
- Different thread APIs (Mach threads vs POSIX)
- GPU metrics via Metal instead of DirectX

**Data Models**:
```csharp
public partial class MacOSProcessProvider : IProcessProvider
{
    public async Task<IReadOnlyList<ProcessInfo>> GetProcessesAsync(CancellationToken ct = default)
    {
        // TODO: Implement using proc_listpids + proc_pidinfo
        return Array.Empty<ProcessInfo>();
    }

    public async Task<IReadOnlyList<ModuleInfo>> GetModulesAsync(int pid, CancellationToken ct = default)
    {
        // TODO: Use dyld APIs for loaded dynamic libraries
        return Array.Empty<ModuleInfo>();
    }

    // ... other stubs
}
```

### Linux Provider Scaffold ✓

**LinuxProcessProvider**:
- Interface implementation stub
- Reads from `/proc` filesystem
- Uses `/sys` for additional metrics
- Placeholder for D-Bus integration

**Platform Detection**:
```csharp
#if LINUX
    services.AddSingleton<IProcessProvider, LinuxProcessProvider>();
#endif
```

**Linux Data Sources** (documented):
- `/proc/<pid>/stat` - Process statistics
- `/proc/<pid>/status` - Process status info
- `/proc/<pid>/maps` - Memory mappings (modules)
- `/proc/<pid>/task/` - Thread enumeration
- `/proc/<pid>/environ` - Environment variables
- `/proc/cpuinfo` - CPU information
- `/proc/meminfo` - Memory information
- `/proc/net/` - Network statistics
- systemd D-Bus for service management

**Example Implementation Pattern**:
```csharp
public async Task<IReadOnlyList<ProcessInfo>> GetProcessesAsync(CancellationToken ct = default)
{
    var procDir = new DirectoryInfo("/proc");
    var processes = new List<ProcessInfo>();

    foreach (var pidDir in procDir.GetDirectories())
    {
        if (!int.TryParse(pidDir.Name, out int pid)) continue;

        var statFile = Path.Combine(pidDir.FullName, "stat");
        if (File.Exists(statFile))
        {
            var stat = File.ReadAllText(statFile);
            // Parse stat line to extract process info
            // Format: pid (comm) state ppid ...
        }
    }

    return processes.AsReadOnly();
}
```

### macOS-Specific Patterns ✓

**Conditional TFM in Project File**:
```xml
<!-- Platform.MacOS.csproj -->
<TargetFrameworks>net8.0-macos;net8.0</TargetFrameworks>

<!-- Only build as macOS when on macOS -->
<TargetFrameworks Condition="!$([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform('osx'))">net8.0</TargetFrameworks>
```

This allows:
- Building with `net8.0-macos` on macOS hardware
- Falling back to `net8.0` on Windows/Linux
- Cross-compilation testing

**LibraryImport vs P/Invoke**:
- Prefer `[LibraryImport("libc")]` for modern .NET
- Use `[System.Runtime.InteropServices.DllImport]` for compatibility
- Handle platform-specific library naming (libSystem.dylib vs libc)

**Process Info from macOS APIs**:
```csharp
[LibraryImport("libSystem.dylib")]
private static partial int getpid();

[LibraryImport("libSystem.dylib")]
private static partial int getppid();

// proc_pidinfo requires more complex marshaling
[DllImport("libSystem.dylib")]
private static extern int proc_pidinfo(
    int pid,
    int flavor,
    ulong arg,
    IntPtr buffer,
    int bufferSize);
```

### Cross-Platform Build Verification ✓

**Solution Structure**:
```
NexusMonitor.sln
├── NexusMonitor.Core               (net8.0 - all platforms)
├── NexusMonitor.Platform.Windows   (net8.0-windows)
├── NexusMonitor.Platform.MacOS     (net8.0-macos / net8.0)
├── NexusMonitor.Platform.Linux     (net8.0)
└── NexusMonitor.UI                 (platform-conditional)
```

**Build Commands**:
```bash
# Windows
dotnet build NexusMonitor.sln

# macOS (if available)
dotnet build NexusMonitor.sln --os osx

# Linux
dotnet build NexusMonitor.sln
```

**Compilation Status**:
- ✓ Windows: 0 errors, 0 warnings
- ✓ macOS scaffold: builds (stubs only)
- ✓ Linux scaffold: builds (stubs only)
- ✓ Core: fully portable

## Architecture Enhancements

### Platform Abstraction

All platform-specific code is behind interfaces:
- `IProcessProvider` - Process enumeration/control
- `ISystemMetricsProvider` - System-wide metrics
- `IServicesProvider` - Service management (Windows-only currently)

**Conditional DI Registration**:
```csharp
public override void OnFrameworkInitializationCompleted()
{
    Services = BuildServices();

#if WINDOWS
    // Register Windows providers
#elif MACOS
    // Register macOS providers
#else
    // Register Linux providers
#endif
}
```

### Error Handling Strategy

**NotImplementedException vs Mock Data**:
- macOS/Linux providers can either:
  - Throw `NotImplementedException` (fail fast)
  - Return mock data (graceful degradation)
  - Current approach: both (feature-dependent)

**UI Adaptation**:
- Feature availability checks in ViewModels
- Hide unsupported features on Linux/macOS
- Show "Coming Soon" placeholders
- Fallback to mock data for development

## Performance Characteristics

**Memory Mapping**:
- macOS process parsing more complex than Windows
- More frequent syscalls for detailed metrics
- Consider caching intervals

**Privileged Access**:
- Some macOS metrics require elevated privileges
- Linux metrics mostly unprivileged (via /proc)
- Service management always requires privileges

## Files Created/Modified

```
src/NexusMonitor.Platform.MacOS/
├── MacOSProcessProvider.cs (NEW - scaffold)
├── MacOSSystemMetricsProvider.cs (NEW - scaffold)
├── MacOSServicesProvider.cs (NEW - scaffold)
└── Native/
    ├── LibSystem.cs (NEW - P/Invoke declarations)
    └── ProcStructures.cs (NEW - Mach/BSD structures)

src/NexusMonitor.Platform.Linux/
├── LinuxProcessProvider.cs (NEW - scaffold)
├── LinuxSystemMetricsProvider.cs (NEW - scaffold)
├── LinuxServicesProvider.cs (NEW - scaffold)
└── Native/
    └── LibC.cs (NEW - libc declarations)

src/NexusMonitor.Platform.Windows/
└── WindowsProcessProvider.cs (UPDATED - verified working)

src/NexusMonitor.UI/
├── ViewModels/ProcessDetailViewModel.cs (UPDATED)
└── Views/ProcessDetailView.axaml (UPDATED - thread/env tabs)
```

## Testing Strategy

**Mock-Based Testing**:
- Use MockProcessProvider for unit tests
- Mock data consistent across platforms
- Test UI logic independently

**Integration Testing**:
- Windows: Full integration with real APIs
- macOS: Integration test with stubs (return mock data)
- Linux: Integration test with stubs

**Platform-Specific Testing**:
- Would require testing on each OS
- Currently verified on Windows only
- macOS/Linux scaffolds compile but untested

## Known Limitations & Future Work

### macOS Implementation Needed

- [ ] Process enumeration via proc_listpids
- [ ] Process metrics via proc_pidinfo
- [ ] Module enumeration (dyld APIs)
- [ ] Thread enumeration (Mach threads)
- [ ] GPU metrics (Metal framework)
- [ ] CPU temperature (Intel Power Gadget API or IOKit)

### Linux Implementation Needed

- [ ] Process parsing from /proc
- [ ] Performance metrics from /sys
- [ ] Service management via systemd/D-Bus
- [ ] Module enumeration from /proc/[pid]/maps
- [ ] Thread enumeration from /proc/[pid]/task
- [ ] GPU support (varies by driver)

### Cross-Platform Gaps

- No GPU metrics on macOS/Linux yet
- Limited temperature data without system daemons
- Different capabilities per platform
- Need feature detection at runtime

## Next Steps

Phase 6 will focus on:
- Task Manager-style performance tabs with per-device sidebar
- Context menus across all tabs
- Full appearance customization
- Desktop overlay widget
- Performance optimizations

See: [[07-Phase-6-Polish]]
