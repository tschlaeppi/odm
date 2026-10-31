using Microsoft.EntityFrameworkCore;
using Odm.Modules.OsUpdates.Domain;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.OsUpdates.Infrastructure;

/// <summary>OS update runs that failed within the last 7 days.</summary>
internal sealed class UpdateRunAlertSource(OsUpdatesDbContext db) : IAlertSource
{
    public async Task<IReadOnlyList<AlertCandidate>> GetAsync(DateTimeOffset now, CancellationToken ct)
    {
        var since = now.AddDays(-7);
        var failed = await db.Runs.AsNoTracking()
            .Where(r => r.Status == RunStatus.Failed && r.CompletedAt > since)
            .Select(r => new { r.Id, r.DeviceId, r.Message, r.Failed })
            .ToListAsync(ct);

        return failed
            .Select(r => new AlertCandidate(
                AlertTypes.UpdateRunFailed,
                $"osrun:{r.Id:N}",
                r.DeviceId,
                "OS update run",
                $"{(r.Failed is > 0 ? $"{r.Failed} update(s) failed. " : "")}{r.Message}".Trim()))
            .ToList();
    }
}
