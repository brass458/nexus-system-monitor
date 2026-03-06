---
type: note
date: 2026-03-05
project: NexusSystemMonitor
tags: [nexus, quick-reference, dev]
---

# Quick Reference

Dev reference for Nexus System Monitor v0.1.6.

---

## Build & Run

```bash
# Build entire solution (always use this — no --framework flag at solution level)
dotnet build NexusMonitor.sln

# Run on Windows (requires elevation; launch .exe directly via PowerShell if not admin)
dotnet run --project src/NexusMonitor.UI/NexusMonitor.UI.csproj --framework net8.0-windows10.0.17763.0

# Run elevated via PowerShell
Start-Process -Verb RunAs ".\src\NexusMonitor.UI\bin\Debug\net8.0-windows10.0.17763.0\NexusMonitor.exe"

# Run on macOS
dotnet run --project src/NexusMonitor.UI/NexusMonitor.UI.csproj --framework net8.0-macos

# Run on Linux
dotnet run --project src/NexusMonitor.UI/NexusMonitor.UI.csproj --framework net8.0

# Run tests
dotnet test tests/NexusMonitor.Core.Tests/NexusMonitor.Core.Tests.csproj
```

---

## Project Paths

| Item | Path |
|------|------|
| Vault root | `O:\ObsidianVaults\Ideaverse\` |
| Project root | `O:\ObsidianVaults\Ideaverse\Areas\Projects\NexusSystemMonitor\` |
| Solution file | `Areas/Projects/NexusSystemMonitor/NexusMonitor.sln` |
| This docs folder | `Areas/Projects/NexusSystemMonitor/docs/` |
| Session logs | `CC-Session-Logs/` |
| CLAUDE.md | `O:\ObsidianVaults\Ideaverse\CLAUDE.md` |

---

## Solution Structure

```
NexusMonitor.sln
├── src/
│   ├── NexusMonitor.Core/              # Abstractions, models, services (net8.0)
│   ├── NexusMonitor.UI/                # Avalonia app (platform TFM)
│   ├── NexusMonitor.DiskAnalyzer/      # Disk analysis engine
│   ├── NexusMonitor.Platform.Windows/  # Windows: P/Invoke, PDH, WMI
│   ├── NexusMonitor.Platform.MacOS/    # macOS: sysctl, Mach, ObjC
│   └── NexusMonitor.Platform.Linux/    # Linux: procfs, sysfs, multi-init
└── tests/
    └── NexusMonitor.Core.Tests/
```

**Assembly name:** `NexusMonitor`
**avares:// prefix:** `avares://NexusMonitor/Assets/...`

---

## Key Patterns

### DynamicResource (required for theming)
```xml
<!-- CORRECT: updates when theme changes -->
<TextBlock Foreground="{DynamicResource TextPrimaryBrush}" />

<!-- WRONG: resolves once at load, never updates -->
<TextBlock Foreground="{StaticResource TextPrimaryBrush}" />
```
Rule: any brush from `ThemeDictionaries` must be `DynamicResource`.

### Reactive data streams
```csharp
_processProvider
    .GetProcessStream(TimeSpan.FromSeconds(2))
    .ObserveOn(RxApp.MainThreadScheduler)
    .Subscribe(processes => UpdateUI(processes));
// Never add Dispatcher.UIThread.Post inside ObserveOn — redundant
```

### DataGrid sort guard (async FIFO pattern)
```csharp
_restoringSort = true;
col.Sort(direction);  // posts ProcessSort via Dispatcher.UIThread.Post
// Reset AFTER sort callbacks (FIFO = same priority):
Dispatcher.UIThread.Post(() => _restoringSort = false);
// Never reset in finally{} — that runs before the async callbacks
```

### DataGrid column visibility
```csharp
// x:Name on DataGridTextColumn does NOT generate code-behind field
// Pattern: name the DataGrid, iterate columns by header string
foreach (var col in MyDataGrid.Columns)
    if (col.Header?.ToString() == "THROUGHPUT")
        col.IsVisible = showThroughput;
```

### git commands (always use -C, never cd &&)
```bash
# CORRECT — avoids bare repository attack security check
git -C "O:/ObsidianVaults/Ideaverse/Areas/Projects/NexusSystemMonitor" status

# WRONG — triggers hardcoded security check
cd "O:/..." && git status
```

### PowerShell scripts from bash
```bash
# Write to Windows temp (PowerShell can't access bash's /tmp)
cat > "C:/Users/josh/AppData/Local/Temp/script.ps1" << 'EOF'
# ... script ...
EOF
powershell -ExecutionPolicy Bypass -File "C:/Users/josh/AppData/Local/Temp/script.ps1"
```

### Platform compile define
```xml
<!-- Required in BOTH .csproj AND linux-*.pubxml publish profiles -->
<DefineConstants>$(DefineConstants);LINUX</DefineConstants>
```

---

## Adding a New Feature

1. **Define model** in `Core/Models/`
2. **Extend interface** in `Core/Abstractions/` if needed
3. **Implement on each platform** in `Platform.Windows/`, `Platform.MacOS/`, `Platform.Linux/`
4. **Create ViewModel** in `UI/ViewModels/`
5. **Build View** in `UI/Views/`
6. **Register in DI** in `App.axaml.cs`

### Adding a new tab

1. Create `YourViewModel.cs` in `ViewModels/`
2. Create `YourView.axaml` + `YourView.axaml.cs` in `Views/`
3. Add `NavItem` to the appropriate `NavGroup` in `MainViewModel`
4. Register ViewModel as `AddSingleton` in `App.axaml.cs`

---

## Common Avalonia Gotchas

| Issue | Fix |
|-------|-----|
| `x:Name` on `DataGridTextColumn` → no code field | Use `x:Name` on `DataGrid`, iterate `.Columns` by header |
| `col.Sort()` guard fires too early | Reset guard via `Dispatcher.UIThread.Post` (FIFO), not `finally` |
| `DataGridTextColumn` sort handler gets null | Always set explicit `SortMemberPath` |
| `RelativeSource AncestorType=UserControl` in nested template | Add `x:CompileBindings="False"` |
| Color `#CCE0FFFFFF` crashes at startup | Only 3/4/6/8 hex digits valid after `#` |
| `TransparencyLevelHint` XAML type error | Set in code-behind, not XAML |
| Theme doesn't update at runtime | Use `{DynamicResource}` not `{StaticResource}` |

---

## Version Info

| Item | Value |
|------|-------|
| Current version | v0.1.6 |
| Target frameworks | net8.0-windows10.0.17763.0 / net8.0-macos / net8.0 |
| Avalonia UI | 11.2.3 |
| CommunityToolkit.Mvvm | 8.3.2 |
| ReactiveUI | + System.Reactive 6.0.1 |
| LiveChartsCore | 2.0.0-rc4 (SkiaSharp) |
| .NET SDK | 8 |

---

## Build Errors

| Error | Cause | Fix |
|-------|-------|-----|
| Build failed on Linux-specific code | `LINUX` define missing | Add to `.csproj` AND `linux-*.pubxml` |
| `avares://` asset not found | Wrong assembly name | Use `avares://NexusMonitor/...` |
| Missing `using ReactiveUI` | `RxApp` not found | Add `using ReactiveUI;` |
| `AllowUnsafeBlocks` error | `fixed char` in P/Invoke struct | Add to `Platform.Windows.csproj` |
| `dotnet build --framework` fails at solution level | Framework flag not supported at solution level | Use plain `dotnet build NexusMonitor.sln` |
