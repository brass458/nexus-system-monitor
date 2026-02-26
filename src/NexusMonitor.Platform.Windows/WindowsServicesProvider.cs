using System.Runtime.InteropServices;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;
using NexusMonitor.Platform.Windows.Native;

using CoreServiceState     = NexusMonitor.Core.Models.ServiceState;
using CoreServiceStartType = NexusMonitor.Core.Models.ServiceStartType;
using CoreServiceType      = NexusMonitor.Core.Models.ServiceType;

namespace NexusMonitor.Platform.Windows;

/// <summary>
/// Real Windows services provider via direct SCM P/Invoke.
/// Reads all Win32 services (own-process and shared) with name, display name,
/// description, state, start type, binary path and service account.
/// </summary>
public sealed class WindowsServicesProvider : IServicesProvider
{
    // ─── IServicesProvider ────────────────────────────────────────────────────

    public Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<ServiceInfo>>(() => EnumerateAll(), ct);

    public Task StartServiceAsync(string name, CancellationToken ct = default) =>
        Task.Run(() => ControlSvc(name, AdvApi32.SERVICE_START,
            h => AdvApi32.StartServiceW(h, 0, nint.Zero)), ct);

    public Task StopServiceAsync(string name, CancellationToken ct = default) =>
        Task.Run(() => SendControl(name, AdvApi32.SERVICE_STOP,
            AdvApi32.SERVICE_CONTROL_STOP), ct);

    public Task RestartServiceAsync(string name, CancellationToken ct = default) =>
        Task.Run(async () =>
        {
            await StopServiceAsync(name, ct);
            await Task.Delay(2000, ct);
            await StartServiceAsync(name, ct);
        }, ct);

    public Task SetStartTypeAsync(string name, CoreServiceStartType startType, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            uint nativeStart = startType switch
            {
                CoreServiceStartType.Automatic        => NativeServiceStartType.SERVICE_AUTO_START,
                CoreServiceStartType.Manual           => NativeServiceStartType.SERVICE_DEMAND_START,
                CoreServiceStartType.Disabled         => NativeServiceStartType.SERVICE_DISABLED,
                CoreServiceStartType.AutomaticDelayed => NativeServiceStartType.SERVICE_AUTO_START,
                _                                     => NativeServiceStartType.SERVICE_DEMAND_START,
            };

            WithService(name,
                AdvApi32.SERVICE_CHANGE_CONFIG,
                hSvc =>
                {
                    AdvApi32.ChangeServiceConfigW(hSvc,
                        dwServiceType   : AdvApi32.SERVICE_NO_CHANGE,
                        dwStartType     : nativeStart,
                        dwErrorControl  : AdvApi32.SERVICE_NO_CHANGE,
                        lpBinaryPathName: null,
                        lpLoadOrderGroup: null,
                        lpdwTagId       : nint.Zero,
                        lpDependencies  : null,
                        lpServiceStartName: null,
                        lpPassword      : null,
                        lpDisplayName   : null);
                });
        }, ct);

    // ─── Enumeration ──────────────────────────────────────────────────────────

    private static IReadOnlyList<ServiceInfo> EnumerateAll()
    {
        nint hScm = AdvApi32.OpenSCManagerW(null, null,
            AdvApi32.SC_MANAGER_CONNECT | AdvApi32.SC_MANAGER_ENUMERATE_SERVICE);
        if (hScm == nint.Zero) return [];
        try
        {
            return Enumerate(hScm);
        }
        finally { AdvApi32.CloseServiceHandle(hScm); }
    }

    private static List<ServiceInfo> Enumerate(nint hScm)
    {
        // First call: get required buffer size
        uint resumeHandle = 0;
        AdvApi32.EnumServicesStatusExW(hScm, AdvApi32.SC_ENUM_PROCESS_INFO,
            AdvApi32.SERVICE_WIN32, AdvApi32.SERVICE_STATE_ALL,
            nint.Zero, 0, out uint needed, out _, ref resumeHandle, nint.Zero);

        if (needed == 0) return [];

        nint buf = Marshal.AllocHGlobal((int)needed);
        try
        {
            resumeHandle = 0;
            if (!AdvApi32.EnumServicesStatusExW(hScm, AdvApi32.SC_ENUM_PROCESS_INFO,
                    AdvApi32.SERVICE_WIN32, AdvApi32.SERVICE_STATE_ALL,
                    buf, needed, out _, out uint returned,
                    ref resumeHandle, nint.Zero))
                return [];

            var result  = new List<ServiceInfo>((int)returned);
            int stride  = Marshal.SizeOf<ENUM_SERVICE_STATUS_PROCESS>();

            for (int i = 0; i < (int)returned; i++)
            {
                nint ptr   = buf + i * stride;
                var  entry = Marshal.PtrToStructure<ENUM_SERVICE_STATUS_PROCESS>(ptr);
                result.Add(BuildServiceInfo(hScm, entry));
            }
            return result;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static ServiceInfo BuildServiceInfo(nint hScm, ENUM_SERVICE_STATUS_PROCESS entry)
    {
        string svcName    = entry.lpServiceName != nint.Zero
                              ? Marshal.PtrToStringUni(entry.lpServiceName) ?? "" : "";
        string displayName= entry.lpDisplayName != nint.Zero
                              ? Marshal.PtrToStringUni(entry.lpDisplayName) ?? "" : "";

        // ── Map current state ──────────────────────────────────────────────────
        var state = entry.ServiceStatusProcess.dwCurrentState switch
        {
            NativeServiceState.SERVICE_RUNNING          => CoreServiceState.Running,
            NativeServiceState.SERVICE_STOPPED          => CoreServiceState.Stopped,
            NativeServiceState.SERVICE_PAUSED           => CoreServiceState.Paused,
            NativeServiceState.SERVICE_START_PENDING    => CoreServiceState.StartPending,
            NativeServiceState.SERVICE_STOP_PENDING     => CoreServiceState.StopPending,
            _                                           => CoreServiceState.Unknown,
        };

        // ── Map service type ───────────────────────────────────────────────────
        uint typeBits = entry.ServiceStatusProcess.dwServiceType;
        var svcType = (typeBits & 0x10) != 0 ? CoreServiceType.Win32OwnProcess
                    : (typeBits & 0x20) != 0 ? CoreServiceType.Win32ShareProcess
                    : (typeBits & 0x01) != 0 ? CoreServiceType.KernelDriver
                    : (typeBits & 0x02) != 0 ? CoreServiceType.FileSystemDriver
                    : CoreServiceType.Unknown;

        // ── Extended config (binary path, user account, description, start type) ──
        string binaryPath   = "";
        string userAccount  = "";
        string description  = "";
        uint   nativeStart  = NativeServiceStartType.SERVICE_DEMAND_START;
        bool   isDelayed    = false;

        nint hSvc = AdvApi32.OpenServiceW(hScm, svcName,
            AdvApi32.SERVICE_QUERY_CONFIG | AdvApi32.SERVICE_QUERY_STATUS);
        if (hSvc != nint.Zero)
        {
            try
            {
                // QueryServiceConfigW — binary path + start type
                AdvApi32.QueryServiceConfigW(hSvc, nint.Zero, 0, out uint cfgNeeded);
                if (cfgNeeded > 0)
                {
                    nint cfgBuf = Marshal.AllocHGlobal((int)cfgNeeded);
                    try
                    {
                        if (AdvApi32.QueryServiceConfigW(hSvc, cfgBuf, cfgNeeded, out _))
                        {
                            var cfg = Marshal.PtrToStructure<QUERY_SERVICE_CONFIG>(cfgBuf);
                            nativeStart = cfg.dwStartType;
                            binaryPath  = cfg.lpBinaryPathName != nint.Zero
                                            ? Marshal.PtrToStringUni(cfg.lpBinaryPathName) ?? "" : "";
                            userAccount = cfg.lpServiceStartName != nint.Zero
                                            ? Marshal.PtrToStringUni(cfg.lpServiceStartName) ?? "" : "";
                        }
                    }
                    finally { Marshal.FreeHGlobal(cfgBuf); }
                }

                // QueryServiceConfig2W — description
                AdvApi32.QueryServiceConfig2W(hSvc, AdvApi32.SERVICE_CONFIG_DESCRIPTION,
                    nint.Zero, 0, out uint descNeeded);
                if (descNeeded > 0)
                {
                    nint descBuf = Marshal.AllocHGlobal((int)descNeeded);
                    try
                    {
                        if (AdvApi32.QueryServiceConfig2W(hSvc, AdvApi32.SERVICE_CONFIG_DESCRIPTION,
                                descBuf, descNeeded, out _))
                        {
                            // Buffer contains a SERVICE_DESCRIPTION: first field is LPWSTR pointer
                            nint descPtr = Marshal.ReadIntPtr(descBuf);
                            description  = descPtr != nint.Zero
                                             ? Marshal.PtrToStringUni(descPtr) ?? "" : "";
                        }
                    }
                    finally { Marshal.FreeHGlobal(descBuf); }
                }

                // Delayed auto-start — must query while hSvc is still open.
                if (nativeStart == NativeServiceStartType.SERVICE_AUTO_START)
                {
                    AdvApi32.QueryServiceConfig2W(hSvc, AdvApi32.SERVICE_CONFIG_DELAYED_AUTO_START_INFO,
                        nint.Zero, 0, out uint dasNeeded);
                    if (dasNeeded >= 4)
                    {
                        nint dasBuf = Marshal.AllocHGlobal((int)dasNeeded);
                        try
                        {
                            if (AdvApi32.QueryServiceConfig2W(hSvc, AdvApi32.SERVICE_CONFIG_DELAYED_AUTO_START_INFO,
                                    dasBuf, dasNeeded, out _))
                                isDelayed = Marshal.ReadInt32(dasBuf) != 0;
                        }
                        finally { Marshal.FreeHGlobal(dasBuf); }
                    }
                }
            }
            catch { /* access denied for some services */ }
            finally { AdvApi32.CloseServiceHandle(hSvc); }
        }

        var mappedStart = nativeStart switch
        {
            NativeServiceStartType.SERVICE_AUTO_START   => isDelayed
                                                            ? CoreServiceStartType.AutomaticDelayed
                                                            : CoreServiceStartType.Automatic,
            NativeServiceStartType.SERVICE_DEMAND_START => CoreServiceStartType.Manual,
            NativeServiceStartType.SERVICE_DISABLED     => CoreServiceStartType.Disabled,
            _ => CoreServiceStartType.Unknown,
        };

        return new ServiceInfo
        {
            Name        = svcName,
            DisplayName = displayName,
            Description = description,
            State       = state,
            StartType   = mappedStart,
            ServiceType = svcType,
            ProcessId   = (int)entry.ServiceStatusProcess.dwProcessId,
            BinaryPath  = binaryPath,
            UserAccount = userAccount,
        };
    }

    // ─── Control helpers ──────────────────────────────────────────────────────

    private static void ControlSvc(string name, uint accessRight, Action<nint> action)
    {
        WithService(name, accessRight, action);
    }

    private static void SendControl(string name, uint accessRight, uint control)
    {
        WithService(name, accessRight, hSvc =>
        {
            var status = new SERVICE_STATUS();
            AdvApi32.ControlService(hSvc, control, ref status);
        });
    }

    private static void WithService(string name, uint accessRight, Action<nint> action)
    {
        nint hScm = AdvApi32.OpenSCManagerW(null, null, AdvApi32.SC_MANAGER_CONNECT);
        if (hScm == nint.Zero)
            throw new InvalidOperationException($"OpenSCManager failed: {Marshal.GetLastWin32Error()}");
        try
        {
            nint hSvc = AdvApi32.OpenServiceW(hScm, name, accessRight);
            if (hSvc == nint.Zero)
                throw new InvalidOperationException($"OpenService({name}) failed: {Marshal.GetLastWin32Error()}");
            try   { action(hSvc); }
            finally { AdvApi32.CloseServiceHandle(hSvc); }
        }
        finally { AdvApi32.CloseServiceHandle(hScm); }
    }
}
