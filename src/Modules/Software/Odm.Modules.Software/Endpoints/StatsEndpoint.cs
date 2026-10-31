using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Odm.Modules.Software.Application;
using Odm.Modules.Software.Domain;
using Odm.Modules.Software.Infrastructure;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.Software.Endpoints;

/// <summary>Key figures about software distribution (dashboard).</summary>
internal static class StatsEndpoint
{
    private const int MaxFailures = 25;
    private const int MessagePreview = 300;

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/software/stats", async (SoftwareDbContext db, IDeviceDirectory directory, CancellationToken ct) =>
            {
                var installations = db.Installations.AsNoTracking();

                var rows = await installations
                    .GroupBy(i => new { i.PackageId, i.Status, i.Removal })
                    .Select(g => new { g.Key.PackageId, g.Key.Status, g.Key.Removal, Count = g.Count() })
                    .ToListAsync(ct);

                int Sum(Func<InstallStatus, bool, bool> match) =>
                    rows.Where(r => match(r.Status, r.Removal)).Sum(r => r.Count);

                var totals = new InstallTotalsDto(
                    Installed: Sum((s, r) => s == InstallStatus.Installed && !r),
                    Pending: Sum((s, r) => s == InstallStatus.Pending && !r),
                    Running: Sum((s, r) => s == InstallStatus.Running && !r),
                    Failed: Sum((s, r) => s == InstallStatus.Failed && !r),
                    Removing: Sum((s, r) => r && s != InstallStatus.Failed),
                    RemovalFailed: Sum((s, r) => r && s == InstallStatus.Failed));

                var names = await db.Packages.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, ct);
                var perPackage = rows
                    .GroupBy(r => r.PackageId)
                    .Where(g => names.ContainsKey(g.Key))
                    .Select(g => new PackageStatsDto(
                        g.Key, names[g.Key],
                        g.Where(r => r.Status == InstallStatus.Installed && !r.Removal).Sum(r => r.Count),
                        g.Where(r => r.Status is InstallStatus.Pending or InstallStatus.Running).Sum(r => r.Count),
                        g.Where(r => r.Status == InstallStatus.Failed).Sum(r => r.Count)))
                    .OrderByDescending(p => p.Failed)
                    .ThenByDescending(p => p.Installed + p.InProgress)
                    .Take(30)
                    .ToList();

                var failed = await installations
                    .Where(i => i.Status == InstallStatus.Failed)
                    .OrderByDescending(i => i.UpdatedAt)
                    .Take(MaxFailures)
                    .ToListAsync(ct);
                var devices = await directory.GetManyAsync(failed.Select(i => i.DeviceId).Distinct().ToList(), ct);

                var failures = failed.Select(i => new FailureDto(
                    i.Id, i.DeviceId, devices.TryGetValue(i.DeviceId, out var d) ? d.Name : null,
                    i.PackageId, names.GetValueOrDefault(i.PackageId, "?"), i.Removal, i.Attempts,
                    i.Message is { Length: > MessagePreview } m ? m[^MessagePreview..] : i.Message,
                    i.UpdatedAt)).ToList();

                return Results.Ok(new SoftwareStatsDto(
                    names.Count, await db.Assignments.CountAsync(ct), totals, perPackage, failures));
            })
            .RequireAuthorization();
    }
}
