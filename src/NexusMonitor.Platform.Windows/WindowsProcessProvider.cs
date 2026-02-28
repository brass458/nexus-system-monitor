using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Reactive.Linq;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;
using NexusMonitor.Platform.Windows.Native;

namespace NexusMonitor.Platform.Windows;

/// <summary>
/// Real Windows process provider.  Uses System.Diagnostics.Process for managed
/// enumeration and supplements with P/Invoke for IO counters, parent PID and
/// elevation status — things the managed API does not expose.
/// </summary>
public sealed class WindowsProcessProvider : IProcessProvider, IDisposable
{
    // CPU delta tracking: pid → (totalProcessorTime, sampleTime)
    private readonly Dictionary<int, (TimeSpan cpu, DateTime time)> _cpuSamples = new();
    // IO delta tracking: pid → (readBytes, writeBytes, sampleTime)
    private readonly Dictionary<int, (ulong read, ulong write, DateTime time)> _ioSamples = new();
    // Stable per-PID caches (username and commandline never change for a given PID)
    private readonly Dictionary<int, string> _userNameCache    = new();
    private readonly Dictionary<int, string> _commandLineCache = new();

    private static readonly int s_processorCount = Math.Max(1, Environment.ProcessorCount);
    private static readonly int s_currentPid     = Environment.ProcessId;

    // ─── IProcessProvider ─────────────────────────────────────────────────────

    public IObservable<IReadOnlyList<ProcessInfo>> GetProcessStream(TimeSpan interval) =>
        // Observable.Timer fires immediately (delay=0) then on each interval tick,
        // always on a thread-pool thread — never blocks the UI thread at subscription time.
        Observable.Timer(TimeSpan.Zero, interval)
                  .Select(_ => (IReadOnlyList<ProcessInfo>)Snapshot());

    public Task<IReadOnlyList<ProcessInfo>> GetProcessesAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<ProcessInfo>>(() => Snapshot(), ct);

    // ─── Snapshot ─────────────────────────────────────────────────────────────

    private List<ProcessInfo> Snapshot()
    {
        var now = DateTime.UtcNow;
        var procs = Process.GetProcesses();
        var result = new List<ProcessInfo>(procs.Length);

        foreach (var p in procs)
        {
            try   { result.Add(Build(p, now)); }
            catch { /* process exited between enum and open */ }
            finally { p.Dispose(); }
        }

        // Evict stale sample entries for dead processes
        var alive = new HashSet<int>(result.Select(r => r.Pid));
        foreach (var stale in _cpuSamples.Keys.Where(k => !alive.Contains(k)).ToList())
        {
            _cpuSamples.Remove(stale);
            _ioSamples.Remove(stale);
            _userNameCache.Remove(stale);
            _commandLineCache.Remove(stale);
        }

        return result;
    }

    private ProcessInfo Build(Process p, DateTime now)
    {
        int  pid         = p.Id;
        bool accessDenied= false;

        // ── CPU % via TotalProcessorTime delta ────────────────────────────────
        double cpuPercent = 0;
        try
        {
            var totalCpu = p.TotalProcessorTime;
            if (_cpuSamples.TryGetValue(pid, out var prev))
            {
                double wallSec = (now - prev.time).TotalSeconds;
                if (wallSec > 0)
                {
                    cpuPercent = (totalCpu - prev.cpu).TotalSeconds
                                 / wallSec / s_processorCount * 100.0;
                    cpuPercent = Math.Clamp(cpuPercent, 0, 100);
                }
            }
            _cpuSamples[pid] = (totalCpu, now);
        }
        catch { accessDenied = true; }

        // ── Memory ────────────────────────────────────────────────────────────
        long wsBytes = 0, privateBytes = 0, pagedPool = 0;
        try
        {
            wsBytes      = p.WorkingSet64;
            privateBytes = p.PrivateMemorySize64;
            pagedPool    = p.PagedMemorySize64;
        }
        catch { }

        // ── Thread / handle counts ────────────────────────────────────────────
        int threadCount = 0, handleCount = 0;
        try { threadCount = p.Threads.Count; }  catch { }
        try { handleCount = p.HandleCount;   }  catch { }

        // ── Start time ────────────────────────────────────────────────────────
        DateTime startTime = default;
        try { startTime = p.StartTime.ToUniversalTime(); } catch { }

        // ── Image path (best-effort) ──────────────────────────────────────────
        string imagePath = string.Empty;
        try { imagePath = p.MainModule?.FileName ?? string.Empty; } catch { }

        // ── P/Invoke supplement: IO counters, parent PID, elevation, username, cmdline ──
        long          ioReadSec         = 0, ioWriteSec = 0;
        int           parentPid         = 0;
        bool          isElevated        = false;
        string        userName          = string.Empty;
        string        commandLine       = string.Empty;
        long          affinityMask      = 0;
        IoPriority    ioPriority        = IoPriority.Normal;
        MemoryPriority memoryPriority   = MemoryPriority.Normal;
        bool          isEfficiencyMode  = false;

        nint hProcess = Kernel32.OpenProcess(
            Kernel32.PROCESS_QUERY_LIMITED_INFO | Kernel32.PROCESS_VM_READ,
            false, (uint)pid);

        if (hProcess != nint.Zero)
        {
            try
            {
                // IO counters with delta
                if (Kernel32.GetProcessIoCounters(hProcess, out IO_COUNTERS io))
                {
                    if (_ioSamples.TryGetValue(pid, out var prevIo))
                    {
                        double elapsed = (now - prevIo.time).TotalSeconds;
                        if (elapsed > 0)
                        {
                            ioReadSec  = (long)((io.ReadTransferCount  - prevIo.read)  / elapsed);
                            ioWriteSec = (long)((io.WriteTransferCount - prevIo.write) / elapsed);
                        }
                    }
                    _ioSamples[pid] = (io.ReadTransferCount, io.WriteTransferCount, now);
                }

                // Parent PID via NtQueryInformationProcess
                parentPid = GetParentPid(hProcess);

                // Elevation via token
                isElevated = GetIsElevated(hProcess);

                // Username (cached — never changes for a given PID)
                if (!_userNameCache.TryGetValue(pid, out userName!))
                    _userNameCache[pid] = userName = GetUserName(hProcess);

                // CommandLine (cached)
                if (!_commandLineCache.TryGetValue(pid, out commandLine!))
                    _commandLineCache[pid] = commandLine = GetCommandLine(hProcess);
            }
            finally { Kernel32.CloseHandle(hProcess); }
        }

        // ── Phase 7: affinity, IO priority, memory priority, efficiency mode ──
        // These require PROCESS_QUERY_INFORMATION (not LIMITED), so use a separate handle
        nint hQuery = Kernel32.OpenProcess(
            Kernel32.PROCESS_QUERY_INFORMATION,
            false, (uint)pid);

        if (hQuery != nint.Zero)
        {
            try
            {
                if (GetProcessAffinityMask(hQuery, out nuint procAffinity, out _))
                    affinityMask = (long)procAffinity;
                ioPriority       = ReadIoPriority(hQuery);
                memoryPriority   = ReadMemoryPriority(hQuery);
                isEfficiencyMode = ReadEfficiencyMode(hQuery);
            }
            catch { /* access denied or process exited */ }
            finally { Kernel32.CloseHandle(hQuery); }
        }

        // ── File description (version info) ───────────────────────────────────
        string description = string.Empty;
        if (!string.IsNullOrEmpty(imagePath))
        {
            try { description = FileVersionInfo.GetVersionInfo(imagePath).FileDescription ?? string.Empty; }
            catch { }
        }

        string name = p.ProcessName;

        return new ProcessInfo
        {
            Pid                   = pid,
            ParentPid             = parentPid,
            Name                  = name,
            Description           = description,
            ImagePath             = imagePath,
            CommandLine           = commandLine,
            UserName              = userName,
            Category              = Classify(pid, name, imagePath),
            State                 = ProcessState.Running,
            StartTime             = startTime,
            CpuPercent            = cpuPercent,
            ThreadCount           = threadCount,
            HandleCount           = handleCount,
            WorkingSetBytes       = wsBytes,
            PrivateBytesBytes     = privateBytes,
            PagedPoolBytes        = pagedPool,
            IoReadBytesPerSec     = Math.Max(0, ioReadSec),
            IoWriteBytesPerSec    = Math.Max(0, ioWriteSec),
            IsElevated            = isElevated,
            AccessDenied          = accessDenied,
            AffinityMask          = affinityMask,
            CurrentIoPriority     = ioPriority,
            CurrentMemoryPriority = memoryPriority,
            IsEfficiencyMode      = isEfficiencyMode,
        };
    }

    // ─── Helper: parent PID ───────────────────────────────────────────────────

    private static int GetParentPid(nint hProcess)
    {
        // PROCESS_BASIC_INFORMATION: 6 nint fields, last is InheritedFromUniqueProcessId
        int    size = Marshal.SizeOf<PROCESS_BASIC_INFORMATION>();
        nint   buf  = Marshal.AllocHGlobal(size);
        try
        {
            int status = NtDll.NtQueryInformationProcess(
                hProcess, NtDll.PROCESSINFOCLASS.ProcessBasicInformation,
                buf, (uint)size, out _);
            if (status != NtDll.STATUS_SUCCESS) return 0;
            var pbi = Marshal.PtrToStructure<PROCESS_BASIC_INFORMATION>(buf);
            return (int)pbi.InheritedFromUniqueProcessId;
        }
        catch { return 0; }
        finally { Marshal.FreeHGlobal(buf); }
    }

    // ─── Helper: elevation ────────────────────────────────────────────────────

    private static bool GetIsElevated(nint hProcess)
    {
        if (!Kernel32.OpenProcessToken(hProcess, Kernel32.TOKEN_QUERY, out nint hToken))
            return false;
        try
        {
            int  size = Marshal.SizeOf<TOKEN_ELEVATION>();
            nint buf  = Marshal.AllocHGlobal(size);
            try
            {
                if (!AdvApi32.GetTokenInformation(hToken,
                        TOKEN_INFORMATION_CLASS.TokenElevation,
                        buf, (uint)size, out _))
                    return false;
                return Marshal.PtrToStructure<TOKEN_ELEVATION>(buf).TokenIsElevated != 0;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { return false; }
        finally { Kernel32.CloseHandle(hToken); }
    }

    // ─── Process category classification ──────────────────────────────────────

    private static ProcessCategory Classify(int pid, string name, string imagePath)
    {
        if (pid == s_currentPid) return ProcessCategory.CurrentProcess;

        string lname = name.ToLowerInvariant();
        string lpath = imagePath.ToLowerInvariant();

        // Kernel / protected system processes
        if (pid is 0 or 4 || lname is "system" or "registry" or "memory compression"
                           or "smss" or "csrss" or "wininit" or "winlogon")
            return ProcessCategory.SystemKernel;

        // Service hosts and core Windows service processes
        if (lname is "svchost" or "services" or "lsass" or "lsm" or "spoolsv"
                   or "taskhost" or "taskhostw" or "sihost" or "ctfmon")
            return ProcessCategory.WindowsService;

        // System32 / SysWOW64 binaries
        if (lpath.Contains(@"\windows\system32\") || lpath.Contains(@"\windows\syswow64\"))
            return ProcessCategory.WindowsService;

        // .NET managed detection: look for .runtimeconfig.json or .deps.json alongside EXE
        if (!string.IsNullOrEmpty(imagePath))
        {
            try
            {
                if (File.Exists(Path.ChangeExtension(imagePath, ".deps.json"))
                 || File.Exists(Path.ChangeExtension(imagePath, ".runtimeconfig.json")))
                    return ProcessCategory.DotNetManaged;
            }
            catch { }
        }

        return ProcessCategory.UserApplication;
    }

    // ─── Helper: username via TokenUser SID → LookupAccountSidW ─────────────

    private static string GetUserName(nint hProcess)
    {
        if (!Kernel32.OpenProcessToken(hProcess, Kernel32.TOKEN_QUERY, out nint hToken))
            return string.Empty;
        nint tokenBuf = nint.Zero;
        nint nameBuf  = nint.Zero;
        nint domBuf   = nint.Zero;
        try
        {
            // First call: get required TOKEN_USER buffer size
            AdvApi32.GetTokenInformation(hToken, TOKEN_INFORMATION_CLASS.TokenUser,
                nint.Zero, 0, out uint tokenSize);
            if (tokenSize == 0) return string.Empty;

            tokenBuf = Marshal.AllocHGlobal((int)tokenSize);
            if (!AdvApi32.GetTokenInformation(hToken, TOKEN_INFORMATION_CLASS.TokenUser,
                    tokenBuf, tokenSize, out _))
                return string.Empty;

            // TOKEN_USER.User.Sid is the first pointer field
            nint sid = Marshal.ReadIntPtr(tokenBuf);

            int nameLen = 256, domLen = 256;
            nameBuf = Marshal.AllocHGlobal(nameLen * 2);
            domBuf  = Marshal.AllocHGlobal(domLen  * 2);

            if (!AdvApi32.LookupAccountSidW(nint.Zero, sid,
                    nameBuf, ref nameLen, domBuf, ref domLen, out _))
                return string.Empty;

            string name   = Marshal.PtrToStringUni(nameBuf) ?? string.Empty;
            string domain = Marshal.PtrToStringUni(domBuf)  ?? string.Empty;
            return domain.Length > 0 ? $"{domain}\\{name}" : name;
        }
        catch { return string.Empty; }
        finally
        {
            Kernel32.CloseHandle(hToken);
            if (tokenBuf != nint.Zero) Marshal.FreeHGlobal(tokenBuf);
            if (nameBuf  != nint.Zero) Marshal.FreeHGlobal(nameBuf);
            if (domBuf   != nint.Zero) Marshal.FreeHGlobal(domBuf);
        }
    }

    // ─── Helper: CommandLine via NtQueryInformationProcess(ProcessCommandLineInfo) ──

    private static string GetCommandLine(nint hProcess)
    {
        nint buf = nint.Zero;
        try
        {
            // Allocate initial 1 KB buffer; reallocate if the kernel needs more
            int size = 1024;
            buf = Marshal.AllocHGlobal(size);

            int status = NtDll.NtQueryInformationProcess(
                hProcess, NtDll.PROCESSINFOCLASS.ProcessCommandLineInfo,
                buf, (uint)size, out uint needed);

            if (needed > size)
            {
                Marshal.FreeHGlobal(buf); buf = nint.Zero;
                size = (int)needed;
                buf  = Marshal.AllocHGlobal(size);
                status = NtDll.NtQueryInformationProcess(
                    hProcess, NtDll.PROCESSINFOCLASS.ProcessCommandLineInfo,
                    buf, (uint)size, out _);
            }

            if (status != NtDll.STATUS_SUCCESS) return string.Empty;

            var us = Marshal.PtrToStructure<UNICODE_STRING>(buf);
            if (us.Buffer == nint.Zero || us.Length == 0) return string.Empty;

            // Buffer points into our allocation — safe to marshal directly
            return Marshal.PtrToStringUni(us.Buffer, us.Length / 2) ?? string.Empty;
        }
        catch { return string.Empty; }
        finally { if (buf != nint.Zero) Marshal.FreeHGlobal(buf); }
    }

    // ─── Mutation operations ──────────────────────────────────────────────────

    public Task KillProcessAsync(int pid, bool killTree = false, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            if (killTree) KillTree(pid);
            else          KillSingle(pid);
        }, ct);

    private static void KillSingle(int pid)
    {
        nint h = Kernel32.OpenProcess(Kernel32.PROCESS_TERMINATE, false, (uint)pid);
        if (h == nint.Zero)
            throw new InvalidOperationException($"Cannot open process {pid}: {Marshal.GetLastWin32Error()}");
        try
        {
            if (!Kernel32.TerminateProcess(h, 1))
                throw new InvalidOperationException($"TerminateProcess failed: {Marshal.GetLastWin32Error()}");
        }
        finally { Kernel32.CloseHandle(h); }
    }

    private static void KillTree(int pid)
    {
        nint snap = Kernel32.CreateToolhelp32Snapshot(Kernel32.TH32CS_SNAPPROCESS, 0);
        if (snap == Kernel32.INVALID_HANDLE_VALUE) { KillSingle(pid); return; }

        var children = new List<int>();
        try
        {
            var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (Kernel32.Process32FirstW(snap, ref e))
                do
                {
                    if ((int)e.th32ParentProcessID == pid && (int)e.th32ProcessID != pid)
                        children.Add((int)e.th32ProcessID);
                }
                while (Kernel32.Process32NextW(snap, ref e));
        }
        finally { Kernel32.CloseHandle(snap); }

        foreach (int child in children)
            try { KillTree(child); } catch { }

        KillSingle(pid);
    }

    public Task SuspendProcessAsync(int pid, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            nint h = Kernel32.OpenProcess(Kernel32.PROCESS_ALL_ACCESS, false, (uint)pid);
            if (h == nint.Zero) throw new InvalidOperationException($"Cannot open process {pid}");
            try   { NtDll.NtSuspendProcess(h); }
            finally { Kernel32.CloseHandle(h); }
        }, ct);

    public Task ResumeProcessAsync(int pid, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            nint h = Kernel32.OpenProcess(Kernel32.PROCESS_ALL_ACCESS, false, (uint)pid);
            if (h == nint.Zero) throw new InvalidOperationException($"Cannot open process {pid}");
            try   { NtDll.NtResumeProcess(h); }
            finally { Kernel32.CloseHandle(h); }
        }, ct);

    public Task SetPriorityAsync(int pid, ProcessPriority priority, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            nint h = Kernel32.OpenProcess(Kernel32.PROCESS_SET_INFORMATION, false, (uint)pid);
            if (h == nint.Zero) throw new InvalidOperationException($"Cannot open process {pid}");
            uint cls = priority switch
            {
                ProcessPriority.Idle        => Kernel32.IDLE_PRIORITY_CLASS,
                ProcessPriority.BelowNormal => Kernel32.BELOW_NORMAL_PRIORITY_CLASS,
                ProcessPriority.Normal      => Kernel32.NORMAL_PRIORITY_CLASS,
                ProcessPriority.AboveNormal => Kernel32.ABOVE_NORMAL_PRIORITY_CLASS,
                ProcessPriority.High        => Kernel32.HIGH_PRIORITY_CLASS,
                ProcessPriority.RealTime    => Kernel32.REALTIME_PRIORITY_CLASS,
                _                           => Kernel32.NORMAL_PRIORITY_CLASS,
            };
            try   { Kernel32.SetPriorityClass(h, cls); }
            finally { Kernel32.CloseHandle(h); }
        }, ct);

    public Task<IReadOnlyList<ModuleInfo>> GetModulesAsync(int pid, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var result = new List<ModuleInfo>();
            nint hProc = Kernel32.OpenProcess(
                Kernel32.PROCESS_QUERY_INFORMATION | Kernel32.PROCESS_VM_READ, false, (uint)pid);
            if (hProc == nint.Zero) return (IReadOnlyList<ModuleInfo>)result;
            try
            {
                // First call: probe required buffer size using a minimal array
                var probe = new nint[1];
                PsApi.EnumProcessModules(hProc, probe, (uint)(nint.Size), out uint needed);
                if (needed == 0) return result;

                int count = (int)(needed / (uint)nint.Size);
                var handles = new nint[count];
                if (!PsApi.EnumProcessModules(hProc, handles, needed, out _))
                    return result;

                var pathBuf = new char[1024];
                foreach (var hMod in handles)
                {
                    ct.ThrowIfCancellationRequested();
                    uint len = PsApi.GetModuleFileNameExW(hProc, hMod, pathBuf, (uint)pathBuf.Length);
                    if (len == 0) continue;
                    string fullPath = new string(pathBuf, 0, (int)len);
                    result.Add(new ModuleInfo(
                        System.IO.Path.GetFileName(fullPath),
                        fullPath,
                        hMod));
                }
            }
            catch { }
            finally { Kernel32.CloseHandle(hProc); }
            return (IReadOnlyList<ModuleInfo>)result;
        }, ct);
    }

    public Task<IReadOnlyList<ThreadInfo>> GetThreadsAsync(int pid, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var result = new List<ThreadInfo>();
            // TH32CS_SNAPTHREAD ignores the pid parameter — capture all threads, filter by owner
            nint snap = Kernel32.CreateToolhelp32Snapshot(Kernel32.TH32CS_SNAPTHREAD, 0);
            if (snap == Kernel32.INVALID_HANDLE_VALUE) return (IReadOnlyList<ThreadInfo>)result;
            try
            {
                var te = new THREADENTRY32 { dwSize = (uint)Marshal.SizeOf<THREADENTRY32>() };
                if (Kernel32.Thread32First(snap, ref te))
                    do
                    {
                        ct.ThrowIfCancellationRequested();
                        if ((int)te.th32OwnerProcessID == pid)
                            result.Add(new ThreadInfo((int)te.th32ThreadID, pid, te.tpBasePri));
                    }
                    while (Kernel32.Thread32Next(snap, ref te));
            }
            catch (OperationCanceledException) { throw; }
            catch { }
            finally { Kernel32.CloseHandle(snap); }
            return (IReadOnlyList<ThreadInfo>)result;
        }, ct);
    }

    public Task SetAffinityAsync(int pid, long affinityMask, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            nint h = Kernel32.OpenProcess(Kernel32.PROCESS_SET_INFORMATION, false, (uint)pid);
            if (h == nint.Zero) throw new InvalidOperationException($"Cannot open process {pid}");
            try   { Kernel32.SetProcessAffinityMask(h, (nint)affinityMask); }
            finally { Kernel32.CloseHandle(h); }
        }, ct);

    public Task SetIoPriorityAsync(int pid, IoPriority priority, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            nint h = Kernel32.OpenProcess(Kernel32.PROCESS_SET_INFORMATION, false, (uint)pid);
            if (h == nint.Zero) throw new InvalidOperationException($"Cannot open process {pid}");
            try
            {
                int value = (int)priority;
                NtSetInformationProcess(h, NtProcessInfoClass.ProcessIoPriority, ref value, sizeof(int));
            }
            finally { Kernel32.CloseHandle(h); }
        }, ct);

    public Task SetMemoryPriorityAsync(int pid, MemoryPriority priority, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            nint h = Kernel32.OpenProcess(Kernel32.PROCESS_SET_INFORMATION, false, (uint)pid);
            if (h == nint.Zero) throw new InvalidOperationException($"Cannot open process {pid}");
            try
            {
                var info = new MEMORY_PRIORITY_INFORMATION { MemoryPriority = (uint)priority };
                nint buf = Marshal.AllocHGlobal(Marshal.SizeOf<MEMORY_PRIORITY_INFORMATION>());
                try
                {
                    Marshal.StructureToPtr(info, buf, false);
                    SetProcessInformation(h, ProcessInformationClass.ProcessMemoryPriority,
                        buf, (uint)Marshal.SizeOf<MEMORY_PRIORITY_INFORMATION>());
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            finally { Kernel32.CloseHandle(h); }
        }, ct);

    public Task TrimWorkingSetAsync(int pid, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            nint h = Kernel32.OpenProcess(Kernel32.PROCESS_SET_INFORMATION | Kernel32.PROCESS_QUERY_INFORMATION, false, (uint)pid);
            if (h == nint.Zero) throw new InvalidOperationException($"Cannot open process {pid}");
            try   { EmptyWorkingSet(h); }
            finally { Kernel32.CloseHandle(h); }
        }, ct);

    public Task SetEfficiencyModeAsync(int pid, bool enabled, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            nint h = Kernel32.OpenProcess(Kernel32.PROCESS_SET_INFORMATION, false, (uint)pid);
            if (h == nint.Zero) throw new InvalidOperationException($"Cannot open process {pid}");
            try
            {
                // PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 1
                var state = new PROCESS_POWER_THROTTLING_STATE
                {
                    Version     = 1,
                    ControlMask = 1,
                    StateMask   = enabled ? 1u : 0u
                };
                nint buf = Marshal.AllocHGlobal(Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
                try
                {
                    Marshal.StructureToPtr(state, buf, false);
                    SetProcessInformation(h, ProcessInformationClass.ProcessPowerThrottling,
                        buf, (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            finally { Kernel32.CloseHandle(h); }
        }, ct);

    public Task<IReadOnlyList<EnvironmentEntry>> GetEnvironmentAsync(int pid, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var result = new List<EnvironmentEntry>();
            nint hProc = Kernel32.OpenProcess(
                Kernel32.PROCESS_QUERY_INFORMATION | Kernel32.PROCESS_VM_READ, false, (uint)pid);
            if (hProc == nint.Zero) return (IReadOnlyList<EnvironmentEntry>)result;
            try
            {
                // 1. PEB base address via NtQueryInformationProcess
                int pbiSize = Marshal.SizeOf<PROCESS_BASIC_INFORMATION>();
                nint pbiBuffer = Marshal.AllocHGlobal(pbiSize);
                nint pebBase;
                try
                {
                    int status = NtDll.NtQueryInformationProcess(hProc, NtDll.PROCESSINFOCLASS.ProcessBasicInformation, pbiBuffer, (uint)pbiSize, out _);
                    if (status != NtDll.STATUS_SUCCESS) return result;
                    var pbi = Marshal.PtrToStructure<PROCESS_BASIC_INFORMATION>(pbiBuffer);
                    pebBase = pbi.PebBaseAddress;
                }
                finally { Marshal.FreeHGlobal(pbiBuffer); }
                if (pebBase == nint.Zero) return result;

                var ptrBuf = new byte[8];

                // 2. ProcessParameters pointer at PEB+0x20 (x64)
                if (!Kernel32.ReadProcessMemory(hProc, pebBase + 0x20, ptrBuf, 8, out _)) return result;
                nint processParamsPtr = (nint)BitConverter.ToInt64(ptrBuf, 0);

                // 3. Environment pointer at ProcessParameters+0x80
                if (!Kernel32.ReadProcessMemory(hProc, processParamsPtr + 0x80, ptrBuf, 8, out _)) return result;
                nint envPtr = (nint)BitConverter.ToInt64(ptrBuf, 0);

                // 4. EnvironmentSize at ProcessParameters+0x3F0 (Windows 10+)
                var sizeBuf = new byte[8];
                if (!Kernel32.ReadProcessMemory(hProc, processParamsPtr + 0x3F0, sizeBuf, 8, out _)) return result;
                ulong envSize = BitConverter.ToUInt64(sizeBuf, 0);

                // 5. Sanity-check the size: offset 0x3F0 may be garbage on older Windows;
                //    fall back to 32 KB if value is 0 or unreasonably large (> 512 KB).
                const ulong MaxEnvBytes    = 512 * 1024;
                const int   FallbackBytes  = 32  * 1024;
                int readSize = (envSize == 0 || envSize > MaxEnvBytes)
                    ? FallbackBytes
                    : (int)envSize;
                if (readSize < 2) return result;

                // 6. Read env block
                var envBuffer = new byte[readSize];
                if (!Kernel32.ReadProcessMemory(hProc, envPtr, envBuffer, (nuint)readSize, out nuint bytesRead)) return result;

                // 7. Parse UTF-16LE "KEY=VALUE\0" pairs; stop at double-null
                int actualBytes = (int)Math.Min(bytesRead, (nuint)readSize);
                string block = System.Text.Encoding.Unicode.GetString(envBuffer, 0, actualBytes);
                int i = 0;
                while (i < block.Length)
                {
                    int nullPos = block.IndexOf('\0', i);
                    if (nullPos <= i) break;
                    string entry = block[i..nullPos];
                    int eq = entry.IndexOf('=');
                    if (eq > 0)
                        result.Add(new EnvironmentEntry(entry[..eq], entry[(eq + 1)..]));
                    i = nullPos + 1;
                }
            }
            catch { }
            finally { Kernel32.CloseHandle(hProc); }
            return (IReadOnlyList<EnvironmentEntry>)result;
        }, ct);
    }

    // ── Handle Viewer ─────────────────────────────────────────────────────────

    public Task<IReadOnlyList<HandleInfo>> GetHandlesAsync(int pid, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<HandleInfo>>(() => EnumHandles(pid), ct);

    private static IReadOnlyList<HandleInfo> EnumHandles(int pid)
    {
        var result = new List<HandleInfo>();

        // 1. Grow buffer until NtQuerySystemInformation returns STATUS_SUCCESS
        int  bufSize = 4 * 1024 * 1024;
        nint buf     = nint.Zero;
        try
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                buf = Marshal.AllocHGlobal(bufSize);
                int status = NtDll.NtQuerySystemInformation(
                    NtDll.SYSTEM_INFORMATION_CLASS.SystemExtendedHandleInformation,
                    buf, (uint)bufSize, out uint needed);
                if (status == NtDll.STATUS_SUCCESS) break;
                Marshal.FreeHGlobal(buf);
                buf = nint.Zero;
                bufSize = (int)Math.Max(needed + 65536, (uint)(bufSize * 2L));
                if (bufSize > 256 * 1024 * 1024) return result;
            }
            if (buf == nint.Zero) return result;

            // 2. Header: ulong Count at offset 0, ulong Reserved at +8
            ulong count = (ulong)Marshal.ReadInt64(buf, 0);
            if (count == 0) return result;

            int entrySize  = Marshal.SizeOf<SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>();
            int headerSize = 16; // count(8) + reserved(8)

            // 3. Collect entries for this PID (cap at 500)
            var entries = new List<SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>();
            for (ulong i = 0; i < count; i++)
            {
                nint entryPtr = buf + headerSize + (nint)(i * (ulong)entrySize);
                var  entry    = Marshal.PtrToStructure<SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>(entryPtr);
                if ((uint)entry.UniqueProcessId == (uint)pid)
                {
                    entries.Add(entry);
                    if (entries.Count >= 500) break;
                }
            }
            if (entries.Count == 0) return result;

            // 4. Open target process for handle duplication
            nint hTarget = Kernel32.OpenProcess(Kernel32.PROCESS_DUP_HANDLE, false, (uint)pid);
            if (hTarget == nint.Zero) return result;

            nint hSelf = System.Diagnostics.Process.GetCurrentProcess().Handle;
            try
            {
                foreach (var entry in entries)
                {
                    try
                    {
                        // 5a. Duplicate handle into current process
                        int dupStatus = NtDll.NtDuplicateObject(
                            hTarget, entry.HandleValue,
                            hSelf,   out nint dupHandle,
                            0, 0, 0);
                        if (dupStatus != NtDll.STATUS_SUCCESS || dupHandle == nint.Zero)
                            continue;

                        string typeName   = "";
                        string objectName = "";
                        try
                        {
                            // 5b. Get type name
                            typeName = QueryObjectTypeName(dupHandle);
                            // 5c. Only query object name for non-blocking handle types
                            if (typeName is "Key" or "Mutant" or "Event" or
                                "Semaphore" or "Timer" or "Section" or "Directory")
                                objectName = QueryObjectName(dupHandle);
                        }
                        finally { Kernel32.CloseHandle(dupHandle); }

                        result.Add(new HandleInfo(
                            (ulong)entry.HandleValue,
                            typeName,
                            objectName,
                            $"0x{entry.GrantedAccess:X8}"));
                    }
                    catch { /* skip inaccessible handles */ }
                }
            }
            finally { Kernel32.CloseHandle(hTarget); }
        }
        finally
        {
            if (buf != nint.Zero)
                Marshal.FreeHGlobal(buf);
        }

        // 6. Sort by TypeName, then ObjectName
        result.Sort((a, b) =>
        {
            int c = string.Compare(a.TypeName, b.TypeName, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.Compare(a.ObjectName, b.ObjectName, StringComparison.OrdinalIgnoreCase);
        });
        return result;
    }

    private static string QueryObjectTypeName(nint handle)
    {
        const int bufSize = 512;
        nint buf = Marshal.AllocHGlobal(bufSize);
        try
        {
            int status = NtDll.NtQueryObject(
                handle, NtDll.OBJECT_INFORMATION_CLASS.ObjectTypeInformation,
                buf, bufSize, out _);
            if (status != NtDll.STATUS_SUCCESS) return "";
            var us = Marshal.PtrToStructure<UNICODE_STRING>(buf);
            if (us.Length == 0 || us.Buffer == nint.Zero) return "";
            return Marshal.PtrToStringUni(us.Buffer, us.Length / 2) ?? "";
        }
        catch { return ""; }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static string QueryObjectName(nint handle)
    {
        const int bufSize = 1024;
        nint buf = Marshal.AllocHGlobal(bufSize);
        try
        {
            int status = NtDll.NtQueryObject(
                handle, NtDll.OBJECT_INFORMATION_CLASS.ObjectNameInformation,
                buf, bufSize, out _);
            if (status != NtDll.STATUS_SUCCESS) return "";
            var us = Marshal.PtrToStructure<UNICODE_STRING>(buf);
            if (us.Length == 0 || us.Buffer == nint.Zero) return "";
            return Marshal.PtrToStringUni(us.Buffer, us.Length / 2) ?? "";
        }
        catch { return ""; }
        finally { Marshal.FreeHGlobal(buf); }
    }

    // ── Memory Map ────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<MemoryRegionInfo>> GetMemoryMapAsync(int pid, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<MemoryRegionInfo>>(() => EnumMemoryMap(pid), ct);

    private static IReadOnlyList<MemoryRegionInfo> EnumMemoryMap(int pid)
    {
        var result = new List<MemoryRegionInfo>();
        nint hProc = Kernel32.OpenProcess(Kernel32.PROCESS_QUERY_LIMITED_INFO, false, (uint)pid);
        if (hProc == nint.Zero) return result;
        try
        {
            nint mbiSize = (nint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>();
            nint addr    = nint.Zero;
            const ulong MaxAddr = 0x7FFFFFFFFFFFUL;

            while ((ulong)addr < MaxAddr && result.Count < 300)
            {
                nint ret = Kernel32.VirtualQueryEx(hProc, addr, out var mbi, mbiSize);
                if (ret == nint.Zero) break;

                ulong regionSize = (ulong)mbi.RegionSize;
                if (regionSize == 0) break;

                // Advance address before anything else (avoid infinite loop)
                addr = (nint)((ulong)addr + regionSize);

                const uint MEM_FREE = 0x10000;
                if (mbi.State == MEM_FREE) continue;

                string description = "";
                const uint MEM_IMAGE = 0x1000000;
                if (mbi.Type == MEM_IMAGE)
                    description = GetMappedImagePath(hProc, mbi.BaseAddress);

                result.Add(new MemoryRegionInfo(
                    (ulong)mbi.BaseAddress,
                    regionSize,
                    DecodeState(mbi.State),
                    DecodeType(mbi.Type),
                    DecodeProtect(mbi.Protect),
                    description));
            }
        }
        finally { Kernel32.CloseHandle(hProc); }
        return result;
    }

    private static string GetMappedImagePath(nint hProc, nint addr)
    {
        var buf = new char[512];
        uint len = PsApi.GetMappedFileNameW(hProc, addr, buf, (uint)buf.Length);
        if (len == 0) return "";
        var path = new string(buf, 0, (int)len);
        // Convert NT device path → drive letter where possible
        foreach (var drive in System.IO.Directory.GetLogicalDrives())
        {
            string letter = drive.TrimEnd('\\');
            var sb = new System.Text.StringBuilder(512);
            if (QueryDosDevice(letter, sb, (uint)sb.Capacity) > 0)
            {
                string device = sb.ToString();
                if (path.StartsWith(device, StringComparison.OrdinalIgnoreCase))
                    return letter + path[device.Length..];
            }
        }
        return path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDevice(
        string lpDeviceName, System.Text.StringBuilder lpTargetPath, uint ucchMax);

    private static string DecodeState(uint state) => state switch
    {
        0x1000 => "Committed",
        0x2000 => "Reserved",
        _      => $"0x{state:X}",
    };

    private static string DecodeType(uint type) => type switch
    {
        0x1000000 => "Image",
        0x40000   => "Mapped",
        0x20000   => "Private",
        _         => $"0x{type:X}",
    };

    private static string DecodeProtect(uint protect)
    {
        uint   baseFlags = protect & 0xFF;
        bool   isGuard   = (protect & 0x100) != 0;
        string prot = baseFlags switch
        {
            0x01 => "No Access",
            0x02 => "R",
            0x04 => "RW",
            0x08 => "WC",
            0x10 => "RX",
            0x20 => "RWX",
            0x40 => "RX+C",
            0x80 => "RWX+C",
            0    => "-",
            _    => $"0x{protect:X2}",
        };
        return isGuard ? prot + "+G" : prot;
    }

    // ── Phase 7 P/Invoke ──────────────────────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(nint hProcess);

    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationProcess(nint hProcess, int infoClass, ref int info, int infoLen);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(nint hProcess, int infoClass, nint info, uint infoSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessInformation(nint hProcess, int infoClass, nint info, uint infoSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessAffinityMask(nint hProcess,
        out nuint lpProcessAffinityMask, out nuint lpSystemAffinityMask);

    // ── Read current IO priority ───────────────────────────────────────────────

    private static IoPriority ReadIoPriority(nint hProcess)
    {
        int value = (int)IoPriority.Normal;
        NtQueryInformationProcessInt(hProcess, NtProcessInfoClass.ProcessIoPriority, ref value, sizeof(int));
        return (IoPriority)Math.Clamp(value, 0, 3);
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcessInt(nint hProcess, int infoClass, ref int info, int infoLen);

    // ── Read current memory priority ──────────────────────────────────────────

    private static MemoryPriority ReadMemoryPriority(nint hProcess)
    {
        var info = new MEMORY_PRIORITY_INFORMATION { MemoryPriority = (uint)MemoryPriority.Normal };
        nint buf = Marshal.AllocHGlobal(Marshal.SizeOf<MEMORY_PRIORITY_INFORMATION>());
        try
        {
            Marshal.StructureToPtr(info, buf, false);
            GetProcessInformation(hProcess, ProcessInformationClass.ProcessMemoryPriority,
                buf, (uint)Marshal.SizeOf<MEMORY_PRIORITY_INFORMATION>());
            var result = Marshal.PtrToStructure<MEMORY_PRIORITY_INFORMATION>(buf);
            return (MemoryPriority)Math.Clamp((int)result.MemoryPriority, 1, 5);
        }
        catch { return MemoryPriority.Normal; }
        finally { Marshal.FreeHGlobal(buf); }
    }

    // ── Read efficiency mode ───────────────────────────────────────────────────

    private static bool ReadEfficiencyMode(nint hProcess)
    {
        var state = new PROCESS_POWER_THROTTLING_STATE { Version = 1 };
        nint buf = Marshal.AllocHGlobal(Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
        try
        {
            Marshal.StructureToPtr(state, buf, false);
            if (!GetProcessInformation(hProcess, ProcessInformationClass.ProcessPowerThrottling,
                    buf, (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>()))
                return false;
            var result = Marshal.PtrToStructure<PROCESS_POWER_THROTTLING_STATE>(buf);
            return (result.ControlMask & 1) != 0 && (result.StateMask & 1) != 0;
        }
        catch { return false; }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public void Dispose()
    {
        _cpuSamples.Clear();
        _ioSamples.Clear();
        _userNameCache.Clear();
        _commandLineCache.Clear();
    }
}

// ── Phase 7 helper types (file-scoped, inside the namespace) ─────────────────

[StructLayout(LayoutKind.Sequential)]
internal struct MEMORY_PRIORITY_INFORMATION
{
    public uint MemoryPriority;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROCESS_POWER_THROTTLING_STATE
{
    public uint Version;     // = 1
    public uint ControlMask;
    public uint StateMask;
}

internal static class ProcessInformationClass
{
    public const int ProcessMemoryPriority  = 0;
    public const int ProcessPowerThrottling = 4;
}

internal static class NtProcessInfoClass
{
    public const int ProcessIoPriority = 33; // 0x21
}
