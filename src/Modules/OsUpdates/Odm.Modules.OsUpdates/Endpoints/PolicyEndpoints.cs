using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Odm.Modules.OsUpdates.Application;
using Odm.Modules.OsUpdates.Domain;
using Odm.Modules.OsUpdates.Infrastructure;
using Odm.SharedKernel;

namespace Odm.Modules.OsUpdates.Endpoints;

internal static class PolicyEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/os-updates/policies").WithTags("OsUpdates").RequireAuthorization();

        group.MapGet("/", async (OsUpdatesDbContext db, CancellationToken ct) =>
            Results.Ok((await db.Policies.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct)).Select(ToDto)));

        group.MapPost("/", async (PolicyRequest request, OsUpdatesDbContext db, CancellationToken ct) =>
            {
                if (!TryParse(request, out var name, out var mode, out var reboot, out var error))
                {
                    return Results.ValidationProblem(error!);
                }

                var lowered = name.ToLower();
                if (await db.Policies.AnyAsync(p => p.Name.ToLower() == lowered, ct))
                {
                    return Results.Conflict();
                }

                var policy = UpdatePolicy.Create(name, request.Description, mode, reboot);
                db.Policies.Add(policy);
                await db.SaveChangesAsync(ct);
                return Results.Created($"/api/os-updates/policies/{policy.Id}", ToDto(policy));
            })
            .RequireAuthorization(Policies.Operator);

        group.MapPut("/{id:guid}", async (Guid id, PolicyRequest request, OsUpdatesDbContext db, CancellationToken ct) =>
            {
                if (!TryParse(request, out var name, out var mode, out var reboot, out var error))
                {
                    return Results.ValidationProblem(error!);
                }

                var policy = await db.Policies.SingleOrDefaultAsync(p => p.Id == id, ct);
                if (policy is null)
                {
                    return Results.NotFound();
                }

                var lowered = name.ToLower();
                if (await db.Policies.AnyAsync(p => p.Id != id && p.Name.ToLower() == lowered, ct))
                {
                    return Results.Conflict();
                }

                policy.Update(name, request.Description, mode, reboot);
                await db.SaveChangesAsync(ct);
                return Results.Ok(ToDto(policy));
            })
            .RequireAuthorization(Policies.Operator);

        group.MapDelete("/{id:guid}", async (Guid id, OsUpdatesDbContext db, CancellationToken ct) =>
            {
                await db.Assignments.Where(a => a.Scope == AssignmentScope.Policy && a.ParentId == id).ExecuteDeleteAsync(ct);
                var deleted = await db.Policies.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
                return deleted == 0 ? Results.NotFound() : Results.NoContent();
            })
            .RequireAuthorization(Policies.Operator);
    }

    private static PolicyDto ToDto(UpdatePolicy p) => new(p.Id, p.Name, p.Description, p.Mode.ToString(), p.Reboot.ToString());

    private static bool TryParse(
        PolicyRequest request, out string name, out UpdateMode mode, out RebootBehavior reboot, out Dictionary<string, string[]>? errors)
    {
        errors = new Dictionary<string, string[]>();
        name = request.Name?.Trim() ?? "";

        if (name.Length is 0 or > 128) errors["name"] = ["Name ist erforderlich (höchstens 128 Zeichen)."];
        if (request.Description is { Length: > 1024 }) errors["description"] = ["Höchstens 1024 Zeichen."];
        if (!Enum.TryParse(request.Mode, true, out mode) || !Enum.IsDefined(mode)) errors["mode"] = ["Ungültiger Modus."];
        if (!Enum.TryParse(request.Reboot, true, out reboot) || !Enum.IsDefined(reboot)) errors["reboot"] = ["Ungültige Neustart-Regel."];

        if (errors.Count == 0)
        {
            errors = null;
            return true;
        }

        return false;
    }
}
