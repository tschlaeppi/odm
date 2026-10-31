using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Odm.Modules.Software.Application;
using Odm.Modules.Software.Domain;
using Odm.Modules.Software.Infrastructure;
using Odm.SharedKernel.Contracts;
using Odm.SharedKernel.Text;

namespace Odm.Modules.Software.Endpoints;

/// <summary>Compliance: devices whose software state deviates from their assignments.</summary>
internal static class ComplianceEndpoint
{
    private const int MaxCsvRows = 200_000;

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/software/compliance", async (
                SoftwareDbContext db, IDeviceDirectory directory, string? reason, int? take, CancellationToken ct) =>
            {
                var report = await BuildAsync(db, directory, reason, Math.Clamp(take ?? 500, 1, 2000), ct);
                return Results.Ok(report);
            })
            .RequireAuthorization();

        endpoints.MapGet("/software/compliance.csv", async (
                SoftwareDbContext db, IDeviceDirectory directory, string? reason, CancellationToken ct) =>
            {
                var report = await BuildAsync(db, directory, reason, MaxCsvRows, ct);
                var file = $"odm-compliance-{DateTime.UtcNow.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.csv";
                return Results.File(
                    CsvFormat.Build(
                        ["Device", "Package", "Deviation", "Target version", "Installed version", "Attempts", "Updated"],
                        report.Rows.Select(r => new[]
                        {
                            r.DeviceName, r.PackageName, r.Reason, r.TargetVersion, r.InstalledVersion,
                            r.Attempts.ToString(CultureInfo.InvariantCulture), r.UpdatedAt.ToString("u", CultureInfo.InvariantCulture),
                        })),
                    "text/csv; charset=utf-8", file);
            })
            .RequireAuthorization();
    }

    private static async Task<ComplianceDto> BuildAsync(
        SoftwareDbContext db, IDeviceDirectory directory, string? reason, int take, CancellationToken ct)
    {
        // Compliance is judged in memory: the rules compare version IDs and removal flags, which keeps them testable.
        var all = await db.Installations.AsNoTracking()
            .Select(i => new { i.Id, i.DeviceId, i.PackageId, i.Status, i.Removal, i.InstalledVersionId, i.TargetVersionId, i.Attempts, i.UpdatedAt })
            .ToListAsync(ct);

        var deviations = all
            .Select(i => (Row: i, Reason: ComplianceRules.Classify(i.Status, i.Removal, i.InstalledVersionId, i.TargetVersionId)))
            .Where(x => x.Reason is not null)
            .ToList();

        var counts = deviations.GroupBy(x => x.Reason!).ToDictionary(g => g.Key, g => g.Count());
        var shown = deviations
            .Where(x => string.IsNullOrWhiteSpace(reason) || string.Equals(x.Reason, reason.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Row.UpdatedAt)
            .Take(take)
            .ToList();

        var packageIds = shown.Select(x => x.Row.PackageId).Distinct().ToList();
        var names = await db.Packages.AsNoTracking().Where(p => packageIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var versions = await db.Versions.AsNoTracking().Where(v => packageIds.Contains(v.PackageId)).ToDictionaryAsync(v => v.Id, v => v.Version, ct);
        var devices = await directory.GetManyAsync(shown.Select(x => x.Row.DeviceId).Distinct().ToList(), ct);

        var rows = shown.Select(x => new ComplianceRowDto(
            x.Row.DeviceId, devices.TryGetValue(x.Row.DeviceId, out var d) ? d.Name : "?",
            x.Row.PackageId, names.GetValueOrDefault(x.Row.PackageId, "?"), x.Reason!,
            versions.GetValueOrDefault(x.Row.TargetVersionId),
            x.Row.InstalledVersionId is { } v ? versions.GetValueOrDefault(v) : null,
            x.Row.Attempts, x.Row.UpdatedAt)).ToList();

        return new ComplianceDto(all.Count, deviations.Count, counts, rows);
    }
}
