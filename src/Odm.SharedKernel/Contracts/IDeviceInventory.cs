namespace Odm.SharedKernel.Contracts;

public sealed record NetworkAdapterInfo(string Name, string? MacAddress, IReadOnlyList<string> Addresses);

public sealed record DiskInfo(string Name, long TotalBytes, long FreeBytes);

public sealed record InventoryHardware(
    string? Manufacturer, string? Model, string? Cpu, int? CpuCores, long? MemoryBytes,
    string? Architecture, long? DiskTotalBytes, long? DiskFreeBytes,
    string? LastUser = null, DateTimeOffset? LastLoginAt = null, DateTimeOffset? BootTime = null,
    IReadOnlyList<NetworkAdapterInfo>? Network = null, IReadOnlyList<DiskInfo>? Disks = null);

public sealed record InventoryProgram(string Name, string? Version, string? Publisher);

/// <summary>Stores the inventory an agent reports (implemented by the Devices module).</summary>
public interface IDeviceInventory
{
    /// <summary>Replaces the stored hardware data and installed programs of a device by the new snapshot.</summary>
    Task ReplaceAsync(Guid deviceId, InventoryHardware hardware, IReadOnlyList<InventoryProgram> software, CancellationToken ct);
}
