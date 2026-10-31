using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.Devices.Infrastructure;

/// <summary>Devices that have not sent a heartbeat for <c>Notifications:SilentHours</c> hours (default 24).</summary>
internal sealed class DeviceAlertSource(DevicesDbContext db, IConfiguration configuration) : IAlertSource
{
    public async Task<IReadOnlyList<AlertCandidate>> GetAsync(DateTimeOffset now, CancellationToken ct)
    {
        var hours = Math.Max(1, configuration.GetValue("Notifications:SilentHours", 24));
        var cutoff = now.AddHours(-hours);

        // Devices that never reported (e.g. demo data) are not alerted.
        var silent = await db.Devices.AsNoTracking()
            .Where(d => d.LastSeenAt != null && d.LastSeenAt < cutoff)
            .Select(d => new { d.Id, d.Name, d.LastSeenAt })
            .ToListAsync(ct);

        return silent
            .Select(d => new AlertCandidate(
                AlertTypes.DeviceSilent,
                $"silent:{d.Id:N}:{d.LastSeenAt!.Value.UtcTicks}",
                d.Id,
                d.Name,
                $"No heartbeat since {d.LastSeenAt:yyyy-MM-dd HH:mm} UTC (more than {hours} h)."))
            .ToList();
    }
}
