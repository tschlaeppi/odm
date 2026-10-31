namespace Odm.Modules.Devices.Application;

public sealed record NetworkAdapterDto(string Name, string? MacAddress, IReadOnlyList<string> Addresses);

public sealed record DiskDto(string Name, long TotalBytes, long FreeBytes);

public sealed record HardwareDto(
    DateTimeOffset CollectedAt, string? Manufacturer, string? Model, string? Cpu, int? CpuCores, long? MemoryBytes,
    string? Architecture, long? DiskTotalBytes, long? DiskFreeBytes,
    string? LastUser = null, DateTimeOffset? LastLoginAt = null, DateTimeOffset? BootTime = null,
    IReadOnlyList<NetworkAdapterDto>? Network = null, IReadOnlyList<DiskDto>? Disks = null);

public sealed record InstalledProgramDto(string Name, string? Version, string? Publisher);

public sealed record DeviceInventoryDto(HardwareDto? Hardware, IReadOnlyList<InstalledProgramDto> Software);

/// <summary>A program (name + version) and on how many devices it is installed.</summary>
public sealed record SoftwareSummaryDto(string Name, string? Version, int Devices);

/// <summary>One installation of a program on one device.</summary>
public sealed record SoftwareRowDto(Guid DeviceId, string DeviceName, string Name, string? Version, string? Publisher);

/// <summary>A device that reported its inventory but does not have a given program.</summary>
public sealed record MissingSoftwareDto(Guid DeviceId, string DeviceName, string OsName, DateTimeOffset CollectedAt);
