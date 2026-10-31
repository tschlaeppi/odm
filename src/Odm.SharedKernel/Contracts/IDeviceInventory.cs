namespace Odm.SharedKernel.Contracts;

public sealed record InventoryHardware(
    string? Manufacturer, string? Model, string? Cpu, int? CpuCores, long? MemoryBytes,
    string? Architecture, long? DiskTotalBytes, long? DiskFreeBytes);

public sealed record InventoryProgram(string Name, string? Version, string? Publisher);

/// <summary>Stores the inventory an agent reports (implemented by the Devices module).</summary>
public interface IDeviceInventory
{
    /// <summary>Replaces the stored hardware data and installed programs of a device by the new snapshot.</summary>
    Task ReplaceAsync(Guid deviceId, InventoryHardware hardware, IReadOnlyList<InventoryProgram> software, CancellationToken ct);
}
