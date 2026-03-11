# Phase 3: Advanced Process Features

**Status**: COMPLETE
**Date**: 2026-02-27

## Overview

Phase 3 extended process monitoring with detailed process information including modules, threads, and environment variables.

## Key Achievements

### Module Enumeration ✓

**Features**:
- List all loaded DLLs/modules in a process
- Module path (full path to DLL file)
- Module base address
- Module size
- File version information

**Implementation**:
```csharp
// Windows: ToolHelp32 snapshot API
using CreateToolhelp32Snapshot
Module32First/Module32Next
// Captures all loaded modules at snapshot time

// Get file info from module path
var vi = FileVersionInfo.GetVersionInfo(modulePath);
// Name, version, company, description
```

**UI Integration**:
- Modules tab in process details panel
- DataGrid listing modules
- Can be exported or analyzed

### Thread Enumeration ✓

**Features**:
- List all threads in a process
- Thread ID (TID)
- Thread state (running, suspended, etc.)
- CPU time per thread
- Context switches
- Thread creation time

**Implementation**:
```csharp
// Windows: ToolHelp32 thread enumeration
using CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD)
Thread32First/Thread32Next

// Get thread details
OpenThread(THREAD_QUERY_INFORMATION)
GetThreadTimes() // Kernel + user time
GetThreadContext() // Thread state
```

**UI Integration**:
- Threads tab in process details
- Real-time thread count
- Thread-level CPU metrics
- Thread states visualization

### Environment Variables ✓

**Features**:
- Enumerate all environment variables for a process
- Variable name and value
- Can span multiple lines (binary-safe)
- Read-only view (changes rare in production monitoring)

**Implementation**:
```csharp
// Windows: Read target process memory
// PEB (Process Environment Block) → Environment block pointer
// Parse environment string table

OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ)
ReadProcessMemory() // Read environment block from PEB+offset
// Parse null-terminated strings
```

**UI Integration**:
- Environment Variables tab in process details
- Search/filter environment vars
- Copy values for debugging
- Export environment as text

### Process Detail Panel ✓

**Layout**:
```
┌─────────────────────────────────────┐
│  Process: explorer.exe (PID: 1234)  │
├──────────┬──────────────────────────┤
│ Modules  │ Details panel             │
│ Threads  │ DataGrid of modules       │
│ Environ  │ Name, Path, Size, Ver     │
│          │                          │
│          │ [Scroll to see all]      │
└──────────┴──────────────────────────┘
```

- Tabbed interface for Modules, Threads, Environment
- Each tab has its own DataGrid
- Real-time updates
- Details shown in right panel

### Process Tree (Bonus) ✓

**Features**:
- Display parent-child process relationships
- Tree view visualization
- Parent process highlighted
- Can expand/collapse branches
- Useful for understanding process hierarchy

## Architecture

### New Abstractions

**Updated IProcessProvider**:
```csharp
Task<IReadOnlyList<ModuleInfo>> GetModulesAsync(int pid);
Task<IReadOnlyList<ThreadInfo>> GetThreadsAsync(int pid);
Task<IReadOnlyList<EnvironmentEntry>> GetEnvironmentAsync(int pid);
```

**New Models**:
```csharp
public record ModuleInfo(
    string Name,
    string Path,
    long BaseAddress,
    long Size,
    string? Version,
    string? FileDescription
);

public record ThreadInfo(
    int ThreadId,
    ThreadState State,
    TimeSpan UserTime,
    TimeSpan KernelTime,
    int ContextSwitches,
    DateTime CreationTime
);

public record EnvironmentEntry(string Name, string Value);
```

### ViewModels

**ProcessDetailViewModel**:
- `SelectedProcess` property
- `Modules`, `Threads`, `Environment` collections
- Load/refresh commands
- Search filtering for modules and environment

**ProcessesViewModel** (Extended):
- Track selected process
- Load detail data on selection change
- Handle multi-process selection

## Implementation Details

### Module Loading Challenges

- Snapshot might miss dynamically loaded modules
- DLL path might require file system check
- Version info parsing (VS_VERSIONINFO structure)
- Unicode vs ANSI string handling

**Solution**:
- Take Tool Help snapshot
- For each module, verify file still exists
- If exists, get FileVersionInfo
- Handle missing/unreadable files gracefully

### Thread Time Calculation

- GetThreadTimes gives accumulated time since thread creation
- Need to track deltas for "current" CPU usage
- User time + Kernel time = total CPU time
- Divide by elapsed time to get percentage

**Solution**:
- Cache previous thread times
- Calculate deltas on each snapshot
- Publish rate as % of available CPU

### Environment Block Reading

- PEB location varies by process bitness (x86 vs x64)
- Environment block is in target process memory space
- Need cross-process memory read via ReadProcessMemory
- Environment is null-terminated wide-char strings
- Last entry is double-null

**Solution**:
- Open target process with VM_READ access
- Read PEB from target
- Find environment block pointer (RTL_USER_PROCESS_PARAMETERS)
- Read environment block in chunks
- Parse as null-terminated strings

## Error Handling

- Process might exit during enumeration
- Modules might unload during snapshot
- Environment block might be paged out
- Threads might terminate

**Pattern**:
```csharp
try
{
    var details = await GetModulesAsync(pid);
    Modules = new(details);
}
catch (ProcessAccessDeniedException)
{
    // Admin required
    Modules = new();
    ErrorMessage = "Administrator access required";
}
catch (ProcessExitedException)
{
    // Already dead
    Modules = new();
}
```

## Performance Impact

- Module enumeration: ~50-100ms per process
- Thread enumeration: ~20-50ms per process
- Environment read: ~30-100ms per process
- Only done on-demand (when detail view opened)
- Cached until process restarts/new PID

## Testing

- Enumerated modules matches LoadedModules in debugger
- Thread count matches Windows Task Manager
- Environment variables match cmd.exe output
- File version info accurate

## Files Modified/Created

```
src/NexusMonitor.Core/
├── Models/ModuleInfo.cs (NEW)
├── Models/ThreadInfo.cs (NEW)
├── Models/EnvironmentEntry.cs (NEW)
└── Abstractions/IProcessProvider.cs (UPDATED)

src/NexusMonitor.Platform.Windows/
├── WindowsProcessProvider.cs (UPDATED - implement new methods)
└── Native/ToolHelp32.cs (NEW - TH32 structures)

src/NexusMonitor.UI/
├── ViewModels/ProcessDetailViewModel.cs (NEW)
├── Views/ProcessDetailView.axaml (NEW)
└── Views/ProcessesView.axaml (UPDATED - add detail panel)
```

## Next Steps

Phase 4 will add:
- Process priority and affinity management
- CPU/memory limits
- Optimization recommendations
- Process grouping and analysis
- Settings/preferences system
- Theme customization

See: [[05-Phase-4-Advanced-Features]]
