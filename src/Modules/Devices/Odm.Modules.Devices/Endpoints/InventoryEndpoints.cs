using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Odm.Modules.Devices.Application;
using Odm.Modules.Devices.Infrastructure;

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

            var hardware = await db.Hardware.AsNoTracking().Where(h => h.DeviceId == id)
                .Select(h => new HardwareDto(
                    h.CollectedAt, h.Manufacturer, h.Model, h.Cpu, h.CpuCores, h.MemoryBytes, h.Architecture,
                    h.DiskTotalBytes, h.DiskFreeBytes))
                .SingleOrDefaultAsync(ct);
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
            var summary = await query
                .GroupBy(s => new { s.Name, s.Version })
                .Select(g => new SoftwareSummaryDto(g.Key.Name, g.Key.Version, g.Select(s => s.DeviceId).Distinct().Count()))
                .OrderBy(s => s.Name).ThenBy(s => s.Version)
                .Take(Math.Clamp(take ?? 500, 1, 2000))
                .ToListAsync(ct);
            return Results.Ok(summary);
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

            var csv = new StringBuilder("﻿Device,Software,Version,Publisher\r\n");
            foreach (var row in rows)
            {
                csv.Append(string.Join(',', Cell(row.DeviceName), Cell(row.Name), Cell(row.Version), Cell(row.Publisher))).Append("\r\n");
            }

            var file = $"odm-software-{DateTime.UtcNow.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.csv";
            return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", file);
        });
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
            .Join(db.Devices.AsNoTracking(), s => s.DeviceId, d => d.Id,
                (s, d) => new SoftwareRowDto(d.Id, d.Name, s.Name, s.Version, s.Publisher))
            .OrderBy(r => r.Name).ThenBy(r => r.Version).ThenBy(r => r.DeviceName);
    }

    /// <summary>CSV cell; values starting with a formula character are neutralised (CSV injection in spreadsheets).</summary>
    internal static string Cell(string? value)
    {
        var text = value ?? "";
        if (text.Length > 0 && "=+-@\t\r".Contains(text[0]))
        {
            text = "'" + text;
        }

        return text.Contains(',') || text.Contains('"') || text.Contains('\n') || text.Contains('\r')
            ? "\"" + text.Replace("\"", "\"\"") + "\""
            : text;
    }
}
