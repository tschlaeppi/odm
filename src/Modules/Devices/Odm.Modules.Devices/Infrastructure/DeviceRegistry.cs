using Microsoft.EntityFrameworkCore;
using Odm.Modules.Devices.Domain;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.Devices.Infrastructure;

internal sealed class DeviceRegistry(DevicesDbContext db) : IDeviceRegistry
{
    public async Task<Guid> RegisterAsync(DeviceReport report, CancellationToken ct)
    {
        var name = report.Hostname.Trim();
        var lowered = name.ToLower();
        var family = ParseFamily(report.OsFamily);

        // Neuinstallation eines Agents auf demselben Rechner übernimmt das bestehende Gerät.
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Name.ToLower() == lowered, ct);
        if (device is null)
        {
            device = Device.Register(name, family, report.OsName, report.OsVersion);
            db.Devices.Add(device);
        }

        device.ApplyReport(family, report.OsName, report.OsVersion, report.AgentVersion);
        await db.SaveChangesAsync(ct);
        return device.Id;
    }

    public async Task ReportAsync(Guid deviceId, DeviceReport report, CancellationToken ct)
    {
        var device = await db.Devices.SingleOrDefaultAsync(d => d.Id == deviceId, ct);
        if (device is null)
        {
            return;
        }

        // Rechner umbenannt: Namen übernehmen, sofern kein anderes Gerät bereits so heisst
        // (sonst bliebe die Zuordnung bei einer Neuregistrierung nach Name mehrdeutig).
        var hostname = report.Hostname.Trim();
        if (hostname.Length > 0 && hostname != device.Name)
        {
            var lowered = hostname.ToLower();
            var taken = await db.Devices.AnyAsync(d => d.Id != deviceId && d.Name.ToLower() == lowered, ct);
            if (!taken)
            {
                device.Rename(hostname);
            }
        }

        device.ApplyReport(ParseFamily(report.OsFamily), report.OsName, report.OsVersion, report.AgentVersion);
        await db.SaveChangesAsync(ct);
    }

    private static OsFamily ParseFamily(string family) =>
        Enum.TryParse<OsFamily>(family, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ArgumentException("Nicht unterstützte OS-Familie.", nameof(family));
}
