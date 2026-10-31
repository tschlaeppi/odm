using Microsoft.EntityFrameworkCore;
using Odm.Modules.Devices.Domain;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.Devices.Infrastructure;

internal sealed class DeviceInventoryStore(DevicesDbContext db) : IDeviceInventory
{
    public const int MaxPrograms = 5000;

    public async Task ReplaceAsync(
        Guid deviceId, InventoryHardware hardware, IReadOnlyList<InventoryProgram> software, CancellationToken ct)
    {
        if (!await db.Devices.AnyAsync(d => d.Id == deviceId, ct))
        {
            return;
        }

        var programs = software
            .Take(MaxPrograms)
            .Select(p => InstalledSoftware.Create(deviceId, p.Name, p.Version, p.Publisher))
            .OfType<InstalledSoftware>()
            .ToList();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var row = await db.Hardware.SingleOrDefaultAsync(h => h.DeviceId == deviceId, ct);
        if (row is null)
        {
            row = DeviceHardware.Create(deviceId);
            db.Hardware.Add(row);
        }

        row.Apply(
            DateTimeOffset.UtcNow, hardware.Manufacturer, hardware.Model, hardware.Cpu, hardware.CpuCores, hardware.MemoryBytes,
            hardware.Architecture, hardware.DiskTotalBytes, hardware.DiskFreeBytes);

        await db.Software.Where(s => s.DeviceId == deviceId).ExecuteDeleteAsync(ct);
        db.Software.AddRange(programs);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
