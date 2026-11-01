using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Odm.Modules.OsUpdates.Domain;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.OsUpdates.Infrastructure;

public sealed class OsUpdateHistoryStore(OsUpdatesDbContext db, IConfiguration configuration) : IOsUpdateHistory
{
    public const int MaxPerDevice = 5000;

    public async Task ReplaceReportedAsync(Guid deviceId, IReadOnlyList<ReportedUpdate> updates, CancellationToken ct)
    {
        var retentionDays = configuration.GetValue("OsUpdates:RunRetentionDays", 365);
        var now = DateTimeOffset.UtcNow;
        var cutoff = retentionDays > 0 ? now.AddDays(-retentionDays) : DateTimeOffset.MinValue;

        var entries = updates
            .Where(u => !string.IsNullOrWhiteSpace(u.Title) && u.InstalledAt >= cutoff && u.InstalledAt <= now.AddDays(1))
            .Select(u => (Title: u.Title.Trim(), u.InstalledAt))
            .Distinct()
            .OrderByDescending(u => u.InstalledAt)
            .Take(MaxPerDevice)
            .Select(u => ReportedUpdateEntry.Create(deviceId, u.Title, u.InstalledAt))
            .ToList();

        await db.ReportedUpdates.Where(e => e.DeviceId == deviceId).ExecuteDeleteAsync(ct);
        db.ReportedUpdates.AddRange(entries);
        await db.SaveChangesAsync(ct);
    }
}
