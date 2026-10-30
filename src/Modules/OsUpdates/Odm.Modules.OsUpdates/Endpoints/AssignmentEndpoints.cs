using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Odm.Modules.OsUpdates.Application;
using Odm.Modules.OsUpdates.Domain;
using Odm.Modules.OsUpdates.Infrastructure;
using Odm.SharedKernel;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.OsUpdates.Endpoints;

/// <summary>Zuweisungen von Richtlinien und Wartungsfenstern an Geräte oder Gruppen.</summary>
internal static class AssignmentEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/os-updates/assignments").WithTags("OsUpdates").RequireAuthorization();

        group.MapGet("/", async (string scope, Guid parentId, OsUpdatesDbContext db, IDeviceDirectory directory, CancellationToken ct) =>
        {
            if (!Enum.TryParse<AssignmentScope>(scope, true, out var parsedScope) || !Enum.IsDefined(parsedScope))
            {
                return Results.BadRequest();
            }

            var rows = await db.Assignments.AsNoTracking()
                .Where(a => a.Scope == parsedScope && a.ParentId == parentId)
                .OrderBy(a => a.CreatedAt)
                .ToListAsync(ct);

            var ids = rows.Where(a => Guid.TryParse(a.Target, out _)).ToList();
            var deviceIds = ids.Where(a => a.TargetKind == TargetKind.Device).Select(a => Guid.Parse(a.Target)).ToList();
            var groupIds = ids.Where(a => a.TargetKind == TargetKind.Group).Select(a => Guid.Parse(a.Target)).ToList();
            var devices = await directory.GetManyAsync(deviceIds, ct);
            var groups = await directory.GetGroupNamesAsync(groupIds, ct);

            return Results.Ok(rows.Select(a =>
            {
                string? name = null;
                if (Guid.TryParse(a.Target, out var id))
                {
                    name = a.TargetKind == TargetKind.Device
                        ? (devices.TryGetValue(id, out var d) ? d.Name : null)
                        : (groups.TryGetValue(id, out var g) ? g : null);
                }

                return new AssignmentDto(a.Id, a.Scope.ToString(), a.ParentId, a.TargetKind.ToString(), a.Target, name);
            }));
        });

        group.MapPost("/", async (AssignmentRequest request, OsUpdatesDbContext db, IDeviceDirectory directory, CancellationToken ct) =>
            {
                if (!Enum.TryParse<AssignmentScope>(request.Scope, true, out var scope) || !Enum.IsDefined(scope) ||
                    !Enum.TryParse<TargetKind>(request.TargetKind, true, out var kind) || !Enum.IsDefined(kind))
                {
                    return Invalid("scope", "Ungültiger Bereich oder Zieltyp.");
                }

                if (!Guid.TryParse(request.Target, out var targetId))
                {
                    return Invalid("target", "Ungültige ID.");
                }

                var exists = scope == AssignmentScope.Policy
                    ? await db.Policies.AnyAsync(p => p.Id == request.ParentId, ct)
                    : await db.Windows.AnyAsync(w => w.Id == request.ParentId, ct);
                if (!exists)
                {
                    return Results.NotFound();
                }

                var targetExists = kind == TargetKind.Device
                    ? await directory.GetAsync(targetId, ct) is not null
                    : await directory.GroupExistsAsync(targetId, ct);
                if (!targetExists)
                {
                    return Invalid("target", "Ziel nicht gefunden.");
                }

                var target = targetId.ToString("D");

                // Je Ziel gilt genau eine Richtlinie; Wartungsfenster dürfen sich beliebig kombinieren.
                var conflict = scope == AssignmentScope.Policy
                    ? await db.Assignments.AnyAsync(a => a.Scope == scope && a.TargetKind == kind && a.Target == target, ct)
                    : await db.Assignments.AnyAsync(a => a.Scope == scope && a.ParentId == request.ParentId && a.TargetKind == kind && a.Target == target, ct);
                if (conflict)
                {
                    return Results.Conflict();
                }

                var assignment = OsAssignment.Create(scope, request.ParentId, kind, targetId);
                db.Assignments.Add(assignment);
                await db.SaveChangesAsync(ct);
                return Results.Created($"/api/os-updates/assignments/{assignment.Id}",
                    new AssignmentDto(assignment.Id, scope.ToString(), assignment.ParentId, kind.ToString(), target, null));
            })
            .RequireAuthorization(Policies.Operator);

        group.MapDelete("/{id:guid}", async (Guid id, OsUpdatesDbContext db, CancellationToken ct) =>
            {
                var deleted = await db.Assignments.Where(a => a.Id == id).ExecuteDeleteAsync(ct);
                return deleted == 0 ? Results.NotFound() : Results.NoContent();
            })
            .RequireAuthorization(Policies.Operator);
    }

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
