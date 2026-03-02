using System.Management;
using Microsoft.Win32;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Platform.Windows;

/// <summary>
/// Queries WMI for static hardware information: CPU, RAM slots, GPU, storage, OS/BIOS.
/// All WMI calls are synchronous; the public API wraps them in Task.Run.
/// </summary>
public sealed class WindowsHardwareInfoProvider
{
    public Task<SystemHardwareInfo> QueryAsync(CancellationToken ct = default) =>
        Task.Run(Query, ct);

    private static SystemHardwareInfo Query()
    {
        var cpu       = QueryCpu();
        var ramSlots  = QueryRamSlots();
        var gpus      = QueryGpus();
        var storage   = QueryStorage();
        var (hostname, osName, osBuild, osArch, uptime) = QueryOs();
        var (biosVendor, biosVersion)                    = QueryBios();
        var (mbMfr, mbModel)                             = QueryMotherboard();

        long totalRam = ramSlots.Sum(s => s.CapacityBytes);
        if (totalRam == 0)
        {
            // Fallback: GC total memory
            totalRam = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        }

        return new SystemHardwareInfo(
            Hostname:                hostname,
            OsName:                  osName,
            OsBuild:                 osBuild,
            OsArchitecture:          osArch,
            Uptime:                  uptime,
            BiosVendor:              biosVendor,
            BiosVersion:             biosVersion,
            MotherboardManufacturer: mbMfr,
            MotherboardModel:        mbModel,
            Cpu:                     cpu,
            TotalRamBytes:           totalRam,
            RamSlots:                ramSlots,
            Gpus:                    gpus,
            Storage:                 storage);
    }

    // -- CPU --------------------------------------------------------------------

    private static CpuHardwareInfo QueryCpu()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            foreach (ManagementObject obj in searcher.Get())
            {
                string name  = obj["Name"]?.ToString()?.Trim() ?? "";
                string arch  = DecodeArch(obj["Architecture"]);
                int cores    = Convert.ToInt32(obj["NumberOfCores"]         ?? 1);
                int logical  = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? 1);
                int l2       = Convert.ToInt32(obj["L2CacheSize"]           ?? 0);
                int l3       = Convert.ToInt32(obj["L3CacheSize"]           ?? 0);
                double clock = Convert.ToDouble(obj["MaxClockSpeed"]        ?? 0);
                string sock  = obj["SocketDesignation"]?.ToString()?.Trim() ?? "";
                string step  = obj["Stepping"]?.ToString()?.Trim()          ?? "";
                return new CpuHardwareInfo(name, arch, cores, logical, l2, l3, clock, sock, step);
            }
        }
        catch { }
        return new CpuHardwareInfo("Unknown", "Unknown", 0, 0, 0, 0, 0, "", "");
    }

    private static string DecodeArch(object? val)
    {
        if (val is null) return "";
        return Convert.ToInt32(val) switch
        {
            0  => "x86",
            5  => "ARM",
            6  => "IA-64",
            9  => "x64",
            12 => "ARM64",
            _  => val.ToString() ?? ""
        };
    }

    // -- RAM --------------------------------------------------------------------

    private static IReadOnlyList<RamSlotInfo> QueryRamSlots()
    {
        var list = new List<RamSlotInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory");
            foreach (ManagementObject obj in searcher.Get())
            {
                string loc  = obj["DeviceLocator"]?.ToString()?.Trim() ?? "";
                long   cap  = Convert.ToInt64(obj["Capacity"]          ?? 0L);
                int    spd  = Convert.ToInt32(obj["Speed"]             ?? 0);
                string type = DecodeMemoryType(obj["SMBIOSMemoryType"]);
                string mfr  = (obj["Manufacturer"]?.ToString()?.Trim()  ?? "").Replace("0000", "").Trim();
                string part = obj["PartNumber"]?.ToString()?.Trim()    ?? "";
                list.Add(new RamSlotInfo(loc, cap, spd, type, mfr, part));
            }
        }
        catch { }
        return list;
    }

    private static string DecodeMemoryType(object? val)
    {
        if (val is null) return "";
        return Convert.ToInt32(val) switch
        {
            20 => "DDR",
            21 => "DDR2",
            22 => "DDR2 FB-DIMM",
            24 => "DDR3",
            26 => "DDR4",
            34 => "DDR5",
            _  => $"Type {val}"
        };
    }

    // -- GPU --------------------------------------------------------------------

    private static IReadOnlyList<GpuHardwareInfo> QueryGpus()
    {
        var list = new List<GpuHardwareInfo>();

        // Build a DriverDesc → 64-bit VRAM lookup from the registry display adapter class.
        // This overrides the WMI AdapterRAM uint32 field which is capped at ~4 GB.
        var registryVram = BuildRegistryVramMap();

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            foreach (ManagementObject obj in searcher.Get())
            {
                string name    = obj["Name"]?.ToString()?.Trim()           ?? "";
                string driver  = obj["DriverVersion"]?.ToString()?.Trim()  ?? "";
                long   vram    = Convert.ToInt64(obj["AdapterRAM"]         ?? 0L);
                string vp      = obj["VideoProcessor"]?.ToString()?.Trim() ?? "";
                string status  = obj["Status"]?.ToString()?.Trim()         ?? "";

                // Use 64-bit registry value if available (avoids 4 GB uint32 truncation).
                if (registryVram.TryGetValue(name, out long regVram) && regVram > vram)
                    vram = regVram;

                list.Add(new GpuHardwareInfo(name, driver, vram, vp, status));
            }
        }
        catch { }
        return list;
    }

    /// <summary>
    /// Reads <c>HardwareInformation.qwMemorySize</c> (REG_QWORD, 64-bit) from every
    /// subkey of the display adapter class GUID in the registry and returns a
    /// DriverDesc → bytes map.  Falls back gracefully when keys are absent or
    /// access is denied.
    /// </summary>
    private static Dictionary<string, long> BuildRegistryVramMap()
    {
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        const string classGuid =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(classGuid);
            if (classKey is null) return map;
            foreach (var subName in classKey.GetSubKeyNames())
            {
                try
                {
                    using var sub = classKey.OpenSubKey(subName);
                    if (sub is null) continue;
                    var driverDesc = sub.GetValue("DriverDesc") as string;
                    var qwMem      = sub.GetValue("HardwareInformation.qwMemorySize");
                    if (driverDesc is null || qwMem is null) continue;
                    long bytes = Convert.ToInt64(qwMem);
                    if (bytes > 0) map[driverDesc] = bytes;
                }
                catch { }
            }
        }
        catch { }
        return map;
    }

    // -- Storage ----------------------------------------------------------------

    private static IReadOnlyList<StorageDriveInfo> QueryStorage()
    {
        var list = new List<StorageDriveInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
            foreach (ManagementObject obj in searcher.Get())
            {
                int    idx    = Convert.ToInt32(obj["Index"]             ?? 0);
                string model  = obj["Model"]?.ToString()?.Trim()         ?? "";
                string iface  = obj["InterfaceType"]?.ToString()?.Trim() ?? "";
                long   size   = Convert.ToInt64(obj["Size"]              ?? 0L);
                string media  = obj["MediaType"]?.ToString()?.Trim()     ?? "";
                string serial = obj["SerialNumber"]?.ToString()?.Trim()  ?? "";
                string status = obj["Status"]?.ToString()?.Trim()        ?? "";
                list.Add(new StorageDriveInfo(idx, model, iface, size, media, serial, status));
            }
        }
        catch { }
        return list;
    }

    // -- OS / System ------------------------------------------------------------

    private static (string hostname, string osName, string osBuild, string osArch, TimeSpan uptime) QueryOs()
    {
        string hostname = "", osName = "", osBuild = "", osArch = "";
        TimeSpan uptime = TimeSpan.Zero;

        try
        {
            using var cs = new ManagementObjectSearcher("SELECT DNSHostName FROM Win32_ComputerSystem");
            foreach (ManagementObject obj in cs.Get())
                hostname = obj["DNSHostName"]?.ToString()?.Trim() ?? Environment.MachineName;
        }
        catch { hostname = Environment.MachineName; }

        try
        {
            using var os = new ManagementObjectSearcher("SELECT Caption,BuildNumber,OSArchitecture,LastBootUpTime FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in os.Get())
            {
                osName  = obj["Caption"]?.ToString()?.Trim() ?? "";
                osBuild = obj["BuildNumber"]?.ToString()?.Trim() ?? "";
                osArch  = obj["OSArchitecture"]?.ToString()?.Trim() ?? "";
                string? bootStr = obj["LastBootUpTime"]?.ToString();
                if (bootStr is not null)
                {
                    try
                    {
                        var bootTime = ManagementDateTimeConverter.ToDateTime(bootStr);
                        uptime = DateTime.Now - bootTime;
                    }
                    catch { }
                }
            }
        }
        catch { }

        return (hostname, osName, osBuild, osArch, uptime);
    }

    // -- BIOS -------------------------------------------------------------------

    private static (string vendor, string version) QueryBios()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer,SMBIOSBIOSVersion FROM Win32_BIOS");
            foreach (ManagementObject obj in searcher.Get())
            {
                string vendor  = obj["Manufacturer"]?.ToString()?.Trim()       ?? "";
                string version = obj["SMBIOSBIOSVersion"]?.ToString()?.Trim()  ?? "";
                return (vendor, version);
            }
        }
        catch { }
        return ("", "");
    }

    // -- Motherboard ------------------------------------------------------------

    private static (string manufacturer, string model) QueryMotherboard()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer,Product FROM Win32_BaseBoard");
            foreach (ManagementObject obj in searcher.Get())
            {
                string mfr   = obj["Manufacturer"]?.ToString()?.Trim() ?? "";
                string model = obj["Product"]?.ToString()?.Trim()      ?? "";
                return (mfr, model);
            }
        }
        catch { }
        return ("", "");
    }
}
