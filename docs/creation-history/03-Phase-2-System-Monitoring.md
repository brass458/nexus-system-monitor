# Phase 2: Real System Monitoring Providers

**Status**: COMPLETE
**Date**: 2026-02-26 to 2026-02-27

## Overview

Phase 2 replaced mock providers with real Windows system monitoring implementations using P/Invoke, Performance Counters, and Windows APIs.

## Key Achievements

### Windows Process Provider ✓

**Real Data Sources**:
- `System.Diagnostics.Process` for basic enumeration
- P/Invoke to Windows APIs for advanced metrics
- NtQueryInformationProcess for parent PID
- GetTokenInformation for elevation status

**Implemented Metrics**:
- Real CPU usage percentage (TotalProcessorTime delta)
- Real memory working set (GetWorkingSet + GlobalMemoryStatusEx)
- I/O read/write rates (GetProcessIoCounters delta)
- Parent process ID
- Process elevation (admin/user)
- Process priority
- Parent PID via native NTAPI

**Key Code Pattern**:
```csharp
// CPU calculation from TotalProcessorTime delta
var delta = proc.TotalProcessorTime - _lastTotalTime[pid];
var elapsed = now - _lastSnapshotTime;
cpuPercent = (delta.TotalMilliseconds / elapsed.TotalMilliseconds) / Environment.ProcessorCount * 100;

// I/O metrics from GetProcessIoCounters
GetProcessIoCounters(proc.Handle, out var ioCounters);
readBytesPerSec = (ioCounters.ReadTransferCount - _lastIoCounters[pid].reads) / elapsed.TotalSeconds;
```

### Windows System Metrics Provider ✓

**CPU Metrics**:
- Real-time CPU utilization via Performance Counters
- Per-core breakdown using PDH (Performance Data Helper)
- Processor time, user time, privileged time

**Memory Metrics**:
- Physical memory usage
- Paged memory
- Available memory
- Using GlobalMemoryStatusEx API

**Disk Metrics**:
- Per-drive I/O rates
- Read/write throughput
- Queue depth
- Multiple disk support via drive letter enumeration

**Network Metrics**:
- Network adapter enumeration
- Bytes sent/received per adapter
- Network interface speed
- Using Win32_NetworkAdapterConfiguration WMI

**GPU Metrics**:
- DXGI-based GPU utilization
- GPU memory usage
- Via Performance Counters (GPU copy engine, decode, etc.)

**Implementation Details**:
```csharp
// Performance Counter initialization
var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
var memCounter = new PerformanceCounter("Memory", "Available MBytes");

// Per-disk counters
foreach (var drive in DriveInfo.GetDrives())
{
    var diskCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", instanceName);
    _diskCounters[driveIndex] = diskCounter;
}

// Per-adapter network counters
foreach (var adapter in _networkAdapters)
{
    var netCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", adapterName);
    _netCounters[adapterName] = netCounter;
}
```

### Windows Services Provider ✓

**Real Service Enumeration**:
- EnumServicesStatusExW P/Invoke for service list
- QueryServiceConfigW for service details
- Service status (running, stopped, paused)
- Service startup type (auto, manual, disabled)
- Service display name and description

**Service Control**:
- Start service (OpenServiceW + StartServiceW)
- Stop service (ControlService with SERVICE_CONTROL_STOP)
- Restart service (stop then start)
- Query service state

**Code Pattern**:
```csharp
[DllImport("advapi32.dll", SetLastError = true)]
public static extern bool EnumServicesStatusExW(
    IntPtr hSCManager, SERVICE_ENUM_TYPE infoLevel, uint dwServiceType,
    uint dwServiceState, byte[] lpServices, uint cbBufSize,
    out uint pcbBytesNeeded, out uint lpServicesReturned,
    ref uint lpResumeHandle, string pszGroupName);
```

## Architecture Improvements

### Conditional Compilation

**Platform-Specific Providers**:
```csharp
#if WINDOWS
    services.AddSingleton<IProcessProvider, WindowsProcessProvider>();
    services.AddSingleton<ISystemMetricsProvider, WindowsSystemMetricsProvider>();
    services.AddSingleton<IServicesProvider, WindowsServicesProvider>();
#elif MACOS
    // macOS providers
#elif LINUX
    // Linux providers
#endif
```

### Mock Providers Still Available

- Mock providers remain for testing UI on non-Windows platforms
- Used for demonstration and development
- Can be swapped in/out via DI configuration

### Error Handling

- Graceful degradation on API failures
- Try-catch around P/Invoke calls
- Default values if metrics unavailable
- Logging for troubleshooting

## P/Invoke Declarations

**Kernel32**:
- GetProcessIoCounters
- GlobalMemoryStatusEx
- GetPerformanceInfo
- CreateToolhelp32Snapshot
- Module32First/Module32Next

**NtDll**:
- NtQueryInformationProcess

**AdvApi32**:
- EnumServicesStatusExW
- QueryServiceConfigW
- OpenServiceW
- StartServiceW
- ControlService

**Psapi**:
- GetWorkingSetEx
- GetProcessMemoryInfo

## Performance Considerations

- Metrics update on 2-second interval (configurable)
- Delta calculations for rate metrics (I/O, network)
- Reuse Performance Counter objects (expensive to create)
- Cache adapter/drive lists (refreshed periodically)
- Async I/O for non-blocking updates

## Testing & Verification

**Build Status**:
```
✓ Build succeeded
✓ 0 errors
✓ 0 warnings
```

**Runtime Verification**:
- Processes tab shows real running processes
- Performance tab displays live graphs
- Services tab enumerates Windows services
- Memory and CPU graphs update in real-time
- Disk and network metrics populate

## File Structure

### Created/Modified Files

```
src/NexusMonitor.Platform.Windows/
├── WindowsProcessProvider.cs       # Real process data
├── WindowsSystemMetricsProvider.cs # Real system metrics
├── WindowsServicesProvider.cs      # Real service enumeration
└── Native/                         # P/Invoke declarations
    ├── Kernel32.cs
    ├── NtDll.cs
    ├── AdvApi32.cs
    ├── Psapi.cs
    └── Structures.cs (enums, structs for APIs)

src/NexusMonitor.UI/
└── Views/PerformanceView.axaml     # Updated for real metrics
```

## Known Limitations

- Windows-only in this phase
- Some metrics require admin privileges (process priority changes)
- Performance Counters can have delay in first reading
- GPU metrics limited by available Windows GPU drivers

## Next Steps

Phase 3 will extend process monitoring with:
- Module enumeration (loaded DLLs)
- Thread enumeration and details
- Environment variables
- Process tree/parent-child relationships
- Advanced process information

See: [[04-Phase-3-Process-Features]]
