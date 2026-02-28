namespace NexusMonitor.Core.Models;

public record CpuHardwareInfo(
    string Name,
    string Architecture,
    int    PhysicalCores,
    int    LogicalCores,
    int    L2CacheKB,
    int    L3CacheKB,
    double MaxClockMhz,
    string Socket,
    string Stepping);

public record RamSlotInfo(
    string DeviceLocator,
    long   CapacityBytes,
    int    SpeedMhz,
    string MemoryType,
    string Manufacturer,
    string PartNumber);

public record GpuHardwareInfo(
    string Name,
    string DriverVersion,
    long   VramBytes,
    string VideoProcessor,
    string Status);

public record StorageDriveInfo(
    int    Index,
    string Model,
    string Interface,
    long   SizeBytes,
    string MediaType,
    string SerialNumber,
    string Status);

public record SystemHardwareInfo(
    string   Hostname,
    string   OsName,
    string   OsBuild,
    string   OsArchitecture,
    TimeSpan Uptime,
    string   BiosVendor,
    string   BiosVersion,
    string   MotherboardManufacturer,
    string   MotherboardModel,
    CpuHardwareInfo                Cpu,
    long                           TotalRamBytes,
    IReadOnlyList<RamSlotInfo>     RamSlots,
    IReadOnlyList<GpuHardwareInfo> Gpus,
    IReadOnlyList<StorageDriveInfo> Storage);
