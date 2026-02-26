using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Mock;

public sealed class MockServicesProvider : IServicesProvider
{
    private static readonly ServiceInfo[] _services =
    [
        Svc("AdobeARMservice",  "Adobe Acrobat Update Service",   ServiceState.Running, ServiceStartType.Automatic),
        Svc("AudioEndpointBuilder","Windows Audio Endpoint Builder",ServiceState.Running,ServiceStartType.Automatic),
        Svc("Audiosrv",         "Windows Audio",                  ServiceState.Running, ServiceStartType.Automatic),
        Svc("BITS",             "Background Intelligent Transfer", ServiceState.Running, ServiceStartType.AutomaticDelayed),
        Svc("BrokerInfrastructure","Background Tasks Infrastructure",ServiceState.Running,ServiceStartType.Automatic),
        Svc("CDPSvc",           "Connected Devices Platform",     ServiceState.Running, ServiceStartType.Automatic),
        Svc("CryptSvc",         "Cryptographic Services",         ServiceState.Running, ServiceStartType.Automatic),
        Svc("DcomLaunch",       "DCOM Server Process Launcher",   ServiceState.Running, ServiceStartType.Automatic),
        Svc("Dhcp",             "DHCP Client",                    ServiceState.Running, ServiceStartType.Automatic),
        Svc("DiagTrack",        "Connected User Experiences and Telemetry",ServiceState.Running,ServiceStartType.Automatic),
        Svc("DispBrokerDesktopSvc","Display Policy Service",      ServiceState.Running, ServiceStartType.Automatic),
        Svc("DPS",              "Diagnostic Policy Service",      ServiceState.Running, ServiceStartType.Automatic),
        Svc("EventLog",         "Windows Event Log",              ServiceState.Running, ServiceStartType.Automatic),
        Svc("FontCache",        "Windows Font Cache Service",     ServiceState.Running, ServiceStartType.Automatic),
        Svc("gpsvc",            "Group Policy Client",            ServiceState.Running, ServiceStartType.Automatic),
        Svc("KeyIso",           "CNG Key Isolation",              ServiceState.Running, ServiceStartType.Manual),
        Svc("LanmanServer",     "Server",                         ServiceState.Running, ServiceStartType.Automatic),
        Svc("LanmanWorkstation","Workstation",                    ServiceState.Running, ServiceStartType.Automatic),
        Svc("mpssvc",           "Windows Defender Firewall",      ServiceState.Running, ServiceStartType.Automatic),
        Svc("Netlogon",         "Netlogon",                       ServiceState.Stopped, ServiceStartType.Manual),
        Svc("Netman",           "Network Connections",            ServiceState.Running, ServiceStartType.Manual),
        Svc("NlaSvc",           "Network Location Awareness",     ServiceState.Running, ServiceStartType.Automatic),
        Svc("nsi",              "Network Store Interface Service",ServiceState.Running, ServiceStartType.Automatic),
        Svc("PlugPlay",         "Plug and Play",                  ServiceState.Running, ServiceStartType.Manual),
        Svc("Power",            "Power",                          ServiceState.Running, ServiceStartType.Automatic),
        Svc("ProfSvc",          "User Profile Service",           ServiceState.Running, ServiceStartType.Automatic),
        Svc("RpcEptMapper",     "RPC Endpoint Mapper",            ServiceState.Running, ServiceStartType.Automatic),
        Svc("RpcSs",            "Remote Procedure Call (RPC)",    ServiceState.Running, ServiceStartType.Automatic),
        Svc("SamSs",            "Security Accounts Manager",      ServiceState.Running, ServiceStartType.Automatic),
        Svc("Schedule",         "Task Scheduler",                 ServiceState.Running, ServiceStartType.Automatic),
        Svc("seclogon",         "Secondary Logon",                ServiceState.Stopped, ServiceStartType.Manual),
        Svc("SENS",             "System Event Notification Service",ServiceState.Running,ServiceStartType.Automatic),
        Svc("ShellHWDetection", "Shell Hardware Detection",       ServiceState.Running, ServiceStartType.Automatic),
        Svc("Spooler",          "Print Spooler",                  ServiceState.Running, ServiceStartType.Automatic),
        Svc("SysMain",          "SysMain (Superfetch)",           ServiceState.Running, ServiceStartType.Automatic),
        Svc("SystemEventsBroker","System Events Broker",          ServiceState.Running, ServiceStartType.Automatic),
        Svc("TokenBroker",      "Web Account Manager",            ServiceState.Running, ServiceStartType.Manual),
        Svc("TrkWks",           "Distributed Link Tracking Client",ServiceState.Running,ServiceStartType.Automatic),
        Svc("UmRdpService",     "Remote Desktop Services UserMode",ServiceState.Stopped,ServiceStartType.Manual),
        Svc("UserManager",      "User Manager",                   ServiceState.Running, ServiceStartType.Automatic),
        Svc("W32Time",          "Windows Time",                   ServiceState.Running, ServiceStartType.Manual),
        Svc("WinDefend",        "Microsoft Defender Antivirus",   ServiceState.Running, ServiceStartType.Automatic),
        Svc("Winmgmt",          "Windows Management Instrumentation",ServiceState.Running,ServiceStartType.Automatic),
        Svc("WSearch",          "Windows Search",                 ServiceState.Running, ServiceStartType.AutomaticDelayed),
        Svc("wuauserv",         "Windows Update",                 ServiceState.Running, ServiceStartType.Manual),
    ];

    public Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(CancellationToken ct = default)
        => Task.FromResult((IReadOnlyList<ServiceInfo>)_services);

    public Task StartServiceAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
    public Task StopServiceAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
    public Task RestartServiceAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
    public Task SetStartTypeAsync(string name, ServiceStartType startType, CancellationToken ct = default) => Task.CompletedTask;

    private static ServiceInfo Svc(string name, string display, ServiceState state, ServiceStartType start) => new()
    {
        Name = name,
        DisplayName = display,
        State = state,
        StartType = start,
        ServiceType = ServiceType.Win32OwnProcess,
        ProcessId = state == ServiceState.Running ? Random.Shared.Next(1000, 9000) : 0,
        BinaryPath = $@"C:\Windows\System32\svchost.exe -k {name}",
        UserAccount = "LocalSystem"
    };
}
