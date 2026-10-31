using Microsoft.EntityFrameworkCore;
using Odm.Modules.Software.Domain;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.Software.Infrastructure;

/// <summary>Installations that failed for good (all attempts used) within the last 7 days.</summary>
internal sealed class InstallationAlertSource(SoftwareDbContext db) : IAlertSource
{
    public async Task<IReadOnlyList<AlertCandidate>> GetAsync(DateTimeOffset now, CancellationToken ct)
    {
        var since = now.AddDays(-7);
        var failed = await db.Installations.AsNoTracking()
            .Where(i => i.Status == InstallStatus.Failed && i.Attempts >= Installation.MaxAttempts && i.UpdatedAt > since)
            .Select(i => new { i.Id, i.DeviceId, i.PackageId, i.Removal, i.Message, i.UpdatedAt })
            .ToListAsync(ct);

        if (failed.Count == 0)
        {
            return [];
        }

        var packageIds = failed.Select(f => f.PackageId).Distinct().ToList();
        var names = await db.Packages.AsNoTracking()
            .Where(p => packageIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        return failed
            .Select(f =>
            {
                var message = f.Message is { Length: > 300 } m ? m[^300..] : f.Message;
                var name = names.GetValueOrDefault(f.PackageId, f.PackageId.ToString());
                return new AlertCandidate(
                    AlertTypes.InstallationFailed,
                    $"install:{f.Id:N}:{f.UpdatedAt.UtcTicks}",
                    f.DeviceId,
                    name,
                    $"{(f.Removal ? "Uninstallation" : "Installation")} failed after {Installation.MaxAttempts} attempts. {message}".Trim());
            })
            .ToList();
    }
}
