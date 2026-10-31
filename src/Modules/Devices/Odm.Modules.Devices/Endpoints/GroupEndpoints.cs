using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Odm.Modules.Devices.Application;
using Odm.Modules.Devices.Domain;
using Odm.Modules.Devices.Infrastructure;
using Odm.SharedKernel;

namespace Odm.Modules.Devices.Endpoints;

internal static class GroupEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/groups").WithTags("Groups").RequireAuthorization();

        group.MapGet("/", async (DevicesDbContext db, CancellationToken ct) =>
        {
            var groups = await db.Groups.AsNoTracking().OrderBy(g => g.Name).ToListAsync(ct);

            var result = new List<GroupDto>(groups.Count);
            foreach (var g in groups)
            {
                var count = await db.Devices.CountAsync(GroupFilter.Build(g.MatchMode, g.Rules), ct);
                result.Add(GroupDto.From(g, count));
            }

            return Results.Ok(result);
        });

        group.MapPost("/", async (GroupRequest request, DevicesDbContext db, CancellationToken ct) =>
            {
                if (!GroupRequestParser.TryParse(request, requireName: true, out var input, out var errors))
                {
                    return Results.ValidationProblem(errors);
                }

                if (await NameTakenAsync(db, input!.Name, exceptId: null, ct))
                {
                    return Results.Conflict();
                }

                var created = DeviceGroup.Create(input.Name, input.Description, input.MatchMode, input.Rules);
                db.Groups.Add(created);
                await db.SaveChangesAsync(ct);

                var count = await db.Devices.CountAsync(GroupFilter.Build(created.MatchMode, created.Rules), ct);
                return Results.Created($"/api/groups/{created.Id}", GroupDto.From(created, count));
            })
            .RequireAuthorization(Policies.Operator);

        group.MapPut("/{id:guid}", async (Guid id, GroupRequest request, DevicesDbContext db, CancellationToken ct) =>
            {
                if (!GroupRequestParser.TryParse(request, requireName: true, out var input, out var errors))
                {
                    return Results.ValidationProblem(errors);
                }

                var existing = await db.Groups.SingleOrDefaultAsync(g => g.Id == id, ct);
                if (existing is null)
                {
                    return Results.NotFound();
                }

                if (await NameTakenAsync(db, input!.Name, exceptId: id, ct))
                {
                    return Results.Conflict();
                }

                existing.Update(input.Name, input.Description, input.MatchMode, input.Rules);
                await db.SaveChangesAsync(ct);

                var count = await db.Devices.CountAsync(GroupFilter.Build(existing.MatchMode, existing.Rules), ct);
                return Results.Ok(GroupDto.From(existing, count));
            })
            .RequireAuthorization(Policies.Operator);

        group.MapDelete("/{id:guid}", async (Guid id, DevicesDbContext db, CancellationToken ct) =>
            {
                var existing = await db.Groups.SingleOrDefaultAsync(g => g.Id == id, ct);
                if (existing is null)
                {
                    return Results.NotFound();
                }

                db.Groups.Remove(existing);
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            })
            .RequireAuthorization(Policies.Operator);

        // Preview while editing: which devices would the rules match? (name not required)
        group.MapPost("/preview", async (GroupRequest request, DevicesDbContext db, CancellationToken ct) =>
            {
                if (!GroupRequestParser.TryParse(request, requireName: false, out var input, out var errors))
                {
                    return Results.ValidationProblem(errors);
                }

                var query = db.Devices.AsNoTracking().Where(GroupFilter.Build(input!.MatchMode, input.Rules));
                return Results.Ok(await DeviceQueries.PageAsync(query, skip: 0, take: 20, ct));
            })
            .RequireAuthorization(Policies.Operator);
    }

    private static Task<bool> NameTakenAsync(DevicesDbContext db, string name, Guid? exceptId, CancellationToken ct)
    {
        var lowered = name.ToLower();
        return db.Groups.AnyAsync(g => g.Name.ToLower() == lowered && g.Id != exceptId, ct);
    }
}
