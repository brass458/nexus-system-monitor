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
        long   ioReadSec   = 0, ioWriteSec = 0;
        int    parentPid   = 0;
        bool   isElevated  = false;
        string userName    = string.Empty;
        string commandLine = string.Empty;

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
            Pid               = pid,
            ParentPid         = parentPid,
            Name              = name,
            Description       = description,
            ImagePath         = imagePath,
            CommandLine       = commandLine,
            UserName          = userName,
            Category          = Classify(pid, name, imagePath),
            State             = ProcessState.Running,
            StartTime         = startTime,
            CpuPercent        = cpuPercent,
            ThreadCount       = threadCount,
            HandleCount       = handleCount,
            WorkingSetBytes   = wsBytes,
            PrivateBytesBytes = privateBytes,
            PagedPoolBytes    = pagedPool,
            IoReadBytesPerSec  = Math.Max(0, ioReadSec),
            IoWriteBytesPerSec = Math.Max(0, ioWriteSec),
            IsElevated        = isElevated,
            AccessDenied      = accessDenied,
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

    public Task SetAffinityAsync(int pid, long affinityMask, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            nint h = Kernel32.OpenProcess(Kernel32.PROCESS_SET_INFORMATION, false, (uint)pid);
            if (h == nint.Zero) throw new InvalidOperationException($"Cannot open process {pid}");
            try   { Kernel32.SetProcessAffinityMask(h, (nint)affinityMask); }
            finally { Kernel32.CloseHandle(h); }
        }, ct);

    public void Dispose()
    {
        _cpuSamples.Clear();
        _ioSamples.Clear();
        _userNameCache.Clear();
        _commandLineCache.Clear();
    }
}
