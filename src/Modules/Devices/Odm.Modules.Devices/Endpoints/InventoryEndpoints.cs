using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Odm.Modules.Devices.Application;
using Odm.Modules.Devices.Infrastructure;
using Odm.SharedKernel.Text;

namespace Odm.Modules.Devices.Endpoints;

/// <summary>Hardware and installed programs reported by the agents, plus reports across all devices.</summary>
internal static class InventoryEndpoints
{
    private const int MaxCsvRows = 200_000;

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/inventory").WithTags("Inventory").RequireAuthorization();

        group.MapGet("/devices/{id:guid}", async (Guid id, DevicesDbContext db, CancellationToken ct) =>
        {
            if (!await db.Devices.AnyAsync(d => d.Id == id, ct))
            {
                return Results.NotFound();
            }

            var row = await db.Hardware.AsNoTracking().SingleOrDefaultAsync(h => h.DeviceId == id, ct);
            var hardware = row is null
                ? null
                : new HardwareDto(
                    row.CollectedAt, row.Manufacturer, row.Model, row.Cpu, row.CpuCores, row.MemoryBytes, row.Architecture,
                    row.DiskTotalBytes, row.DiskFreeBytes, row.LastUser, row.LastLoginAt, row.BootTime,
                    row.Adapters.Select(a => new NetworkAdapterDto(a.Name, a.MacAddress, a.Addresses)).ToList(),
                    row.Volumes.Select(d => new DiskDto(d.Name, d.TotalBytes, d.FreeBytes)).ToList());
            var software = await db.Software.AsNoTracking().Where(s => s.DeviceId == id)
                .OrderBy(s => s.Name)
                .Select(s => new InstalledProgramDto(s.Name, s.Version, s.Publisher))
                .ToListAsync(ct);

            return Results.Ok(new DeviceInventoryDto(hardware, software));
        });

        // Which programs and versions exist, and on how many devices.
        group.MapGet("/software/summary", async (DevicesDbContext db, string? name, int? take, CancellationToken ct) =>
        {
            var query = FilterByName(db.Software.AsNoTracking(), name);
            // Aggregate into an anonymous type first and map afterwards: EF cannot order by a DTO built around an aggregate.
            var groups = await query
                .GroupBy(s => new { s.Name, s.Version })
                .Select(g => new { g.Key.Name, g.Key.Version, Devices = g.Select(s => s.DeviceId).Distinct().Count() })
                .OrderBy(g => g.Name).ThenBy(g => g.Version)
                .Take(Math.Clamp(take ?? 500, 1, 2000))
                .ToListAsync(ct);
            return Results.Ok(groups.Select(g => new SoftwareSummaryDto(g.Name, g.Version, g.Devices)));
        });

        // Devices per program: filter by name (contains) and exact version.
        group.MapGet("/software", async (
            DevicesDbContext db, string? name, string? version, int? skip, int? take, CancellationToken ct) =>
        {
            var rows = await Rows(db, name, version)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 500, 1, 2000))
                .ToListAsync(ct);
            return Results.Ok(rows);
        });

        group.MapGet("/software.csv", async (DevicesDbContext db, string? name, string? version, CancellationToken ct) =>
        {
            var rows = await Rows(db, name, version).Take(MaxCsvRows).ToListAsync(ct);

            var file = $"odm-software-{DateTime.UtcNow.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.csv";
            return Results.File(
                CsvFormat.Build(
                    ["Device", "Software", "Version", "Publisher"],
                    rows.Select(r => new[] { r.DeviceName, r.Name, r.Version, r.Publisher })),
                "text/csv; charset=utf-8", file);
        });

        // Devices that reported an inventory but have no program whose name contains the search term.
        group.MapGet("/software/missing", async (DevicesDbContext db, string? name, int? take, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Program name is required."] });
            }

            return Results.Ok(await Missing(db, name).Take(Math.Clamp(take ?? 500, 1, 2000)).ToListAsync(ct));
        });

        group.MapGet("/software/missing.csv", async (DevicesDbContext db, string? name, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Program name is required."] });
            }

            var rows = await Missing(db, name).Take(MaxCsvRows).ToListAsync(ct);
            var file = $"odm-missing-{DateTime.UtcNow.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.csv";
            return Results.File(
                CsvFormat.Build(["Device", "Operating system", "Inventory collected"],
                    rows.Select(r => new[] { r.DeviceName, r.OsName, r.CollectedAt.ToString("u", CultureInfo.InvariantCulture) })),
                "text/csv; charset=utf-8", file);
        });
    }

    private static IQueryable<MissingSoftwareDto> Missing(DevicesDbContext db, string name)
    {
        var matching = FilterByName(db.Software.AsNoTracking(), name).Select(s => s.DeviceId);
        return db.Hardware.AsNoTracking()
            .Where(h => !matching.Contains(h.DeviceId))
            .Join(db.Devices.AsNoTracking(), h => h.DeviceId, d => d.Id, (h, d) => new { Hardware = h, Device = d })
            .OrderBy(x => x.Device.Name)
            .Select(x => new MissingSoftwareDto(x.Device.Id, x.Device.Name, x.Device.OsName, x.Hardware.CollectedAt));
    }

    private static IQueryable<Domain.InstalledSoftware> FilterByName(IQueryable<Domain.InstalledSoftware> query, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return query;
        }

        var term = name.Trim().ToLower();
        return query.Where(s => s.Name.ToLower().Contains(term));
    }

    private static IQueryable<SoftwareRowDto> Rows(DevicesDbContext db, string? name, string? version)
    {
        var query = FilterByName(db.Software.AsNoTracking(), name);
        if (!string.IsNullOrWhiteSpace(version))
        {
            var exact = version.Trim();
            query = query.Where(s => s.Version == exact);
        }

        return query
            .Join(db.Devices.AsNoTracking(), s => s.DeviceId, d => d.Id, (s, d) => new { Software = s, Device = d })
            .OrderBy(x => x.Software.Name).ThenBy(x => x.Software.Version).ThenBy(x => x.Device.Name)
            .Select(x => new SoftwareRowDto(x.Device.Id, x.Device.Name, x.Software.Name, x.Software.Version, x.Software.Publisher));
    }
}
