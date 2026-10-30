using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Odm.Modules.Devices.Application;
using Odm.Modules.Devices.Infrastructure;
using Odm.SharedKernel;

namespace Odm.Modules.Devices.Endpoints;

internal static class DeviceEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/devices").WithTags("Devices").RequireAuthorization();

        group.MapGet("/", async (
            DevicesDbContext db, int? skip, int? take, string? search, Guid? groupId, CancellationToken ct) =>
        {
            IQueryable<Domain.Device> query = db.Devices.AsNoTracking();

            if (groupId is { } id)
            {
                var deviceGroup = await db.Groups.AsNoTracking().SingleOrDefaultAsync(g => g.Id == id, ct);
                if (deviceGroup is null)
                {
                    return Results.NotFound();
                }

                query = query.Where(GroupFilter.Build(deviceGroup.MatchMode, deviceGroup.Rules));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(d => d.Name.ToLower().Contains(term));
            }

            return Results.Ok(await DeviceQueries.PageAsync(query, skip, take, ct));
        });

        group.MapGet("/{id:guid}", async (Guid id, DevicesDbContext db, CancellationToken ct) =>
        {
            var device = await db.Devices.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
            return device is null ? Results.NotFound() : Results.Ok(DeviceDto.From(device));
        });

        // Gruppen, in denen das Gerät aktuell Mitglied ist (Gruppen sind dynamisch und werden hier ausgewertet).
        group.MapGet("/{id:guid}/groups", async (Guid id, DevicesDbContext db, CancellationToken ct) =>
        {
            if (!await db.Devices.AnyAsync(d => d.Id == id, ct))
            {
                return Results.NotFound();
            }

            var result = new List<DeviceGroupRefDto>();
            foreach (var g in await db.Groups.AsNoTracking().OrderBy(g => g.Name).ToListAsync(ct))
            {
                if (await db.Devices.Where(d => d.Id == id).Where(GroupFilter.Build(g.MatchMode, g.Rules)).AnyAsync(ct))
                {
                    result.Add(new DeviceGroupRefDto(g.Id, g.Name, g.Description));
                }
            }

            return Results.Ok(result);
        });

        group.MapPut("/{id:guid}/assignment", async (
                Guid id, AssignmentRequest request, DevicesDbContext db, CancellationToken ct) =>
            {
                var errors = new Dictionary<string, string[]>();
                if (request.PrimaryUser is { Length: > 256 }) errors["primaryUser"] = ["Höchstens 256 Zeichen."];
                if (request.OrgUnit is { Length: > 256 }) errors["orgUnit"] = ["Höchstens 256 Zeichen."];
                if (request.CostCenter is { Length: > 64 }) errors["costCenter"] = ["Höchstens 64 Zeichen."];
                if (errors.Count > 0)
                {
                    return Results.ValidationProblem(errors);
                }

                var device = await db.Devices.SingleOrDefaultAsync(d => d.Id == id, ct);
                if (device is null)
                {
                    return Results.NotFound();
                }

                device.UpdateAssignment(request.PrimaryUser, request.OrgUnit, request.CostCenter);
                await db.SaveChangesAsync(ct);
                return Results.Ok(DeviceDto.From(device));
            })
            .RequireAuthorization(Policies.Operator);
    }
}
