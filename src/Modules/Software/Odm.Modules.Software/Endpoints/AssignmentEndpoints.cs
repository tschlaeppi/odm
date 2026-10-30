using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Odm.Modules.Software.Application;
using Odm.Modules.Software.Domain;
using Odm.Modules.Software.Infrastructure;
using Odm.SharedKernel;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.Software.Endpoints;

internal static class AssignmentEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var assignments = endpoints.MapGroup("/software/assignments").WithTags("Software").RequireAuthorization();

        assignments.MapGet("/", async (Guid packageId, SoftwareDbContext db, IDeviceDirectory directory, CancellationToken ct) =>
        {
            var rows = await db.Assignments.AsNoTracking()
                .Where(a => a.PackageId == packageId)
                .OrderBy(a => a.CreatedAt)
                .ToListAsync(ct);

            var deviceIds = rows.Where(a => a.TargetKind == TargetKind.Device && Guid.TryParse(a.Target, out _))
                .Select(a => Guid.Parse(a.Target)).ToList();
            var devices = await directory.GetManyAsync(deviceIds, ct);

            return Results.Ok(rows.Select(a => new AssignmentDto(
                a.Id, a.PackageId, a.TargetKind.ToString(), a.Target,
                a.TargetKind == TargetKind.Device && Guid.TryParse(a.Target, out var id) && devices.TryGetValue(id, out var d) ? d.Name : null,
                a.VersionPolicy.ToString(), a.PinnedVersionId, a.CreatedAt)));
        });

        assignments.MapPost("/", async (
                AssignmentRequest request, SoftwareDbContext db, IDeviceDirectory directory,
                DeploymentTrigger trigger, CancellationToken ct) =>
            {
                if (!Enum.TryParse<TargetKind>(request.TargetKind, true, out var kind) || !Enum.IsDefined(kind) ||
                    !Enum.TryParse<VersionPolicy>(request.VersionPolicy, true, out var policy) || !Enum.IsDefined(policy))
                {
                    return Invalid("targetKind", "Ungültiger Zieltyp oder ungültige Versionsregel.");
                }

                var target = request.Target?.Trim() ?? "";
                if (target.Length is 0 or > 256)
                {
                    return Invalid("target", "Ziel ist erforderlich.");
                }

                if (kind != TargetKind.User)
                {
                    if (!Guid.TryParse(target, out var targetId))
                    {
                        return Invalid("target", "Ungültige ID.");
                    }

                    var exists = kind == TargetKind.Device
                        ? await directory.GetAsync(targetId, ct) is not null
                        : await directory.GroupExistsAsync(targetId, ct);
                    if (!exists)
                    {
                        return Invalid("target", "Ziel nicht gefunden.");
                    }

                    target = targetId.ToString("D");
                }

                var package = await db.Packages.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.PackageId, ct);
                if (package is null)
                {
                    return Results.NotFound();
                }

                // Bei Repo-Paketen steht die feste Version in der Paketkonfiguration, nicht in der Zuweisung.
                if (package.Kind.IsRepo())
                {
                    policy = VersionPolicy.Latest;
                }

                var pinError = await ValidatePinAsync(db, request.PackageId, policy, request.PinnedVersionId, ct);
                if (pinError is not null)
                {
                    return Invalid("pinnedVersionId", pinError);
                }

                var normalized = target.ToLowerInvariant();
                if (await db.Assignments.AnyAsync(
                        a => a.PackageId == request.PackageId && a.TargetKind == kind && a.Target == normalized, ct))
                {
                    return Results.Conflict();
                }

                var assignment = Assignment.Create(request.PackageId, kind, target, policy, request.PinnedVersionId);
                db.Assignments.Add(assignment);
                await db.SaveChangesAsync(ct);
                trigger.Request();

                return Results.Created($"/api/software/assignments/{assignment.Id}",
                    new AssignmentDto(assignment.Id, assignment.PackageId, kind.ToString(), assignment.Target, null,
                        policy.ToString(), assignment.PinnedVersionId, assignment.CreatedAt));
            })
            .RequireAuthorization(Policies.Operator);

        assignments.MapPut("/{id:guid}", async (
                Guid id, UpdateAssignmentRequest request, SoftwareDbContext db, DeploymentTrigger trigger, CancellationToken ct) =>
            {
                if (!Enum.TryParse<VersionPolicy>(request.VersionPolicy, true, out var policy) || !Enum.IsDefined(policy))
                {
                    return Invalid("versionPolicy", "Ungültige Versionsregel.");
                }

                var assignment = await db.Assignments.SingleOrDefaultAsync(a => a.Id == id, ct);
                if (assignment is null)
                {
                    return Results.NotFound();
                }

                var assignedPackage = await db.Packages.AsNoTracking().SingleAsync(p => p.Id == assignment.PackageId, ct);
                if (assignedPackage.Kind.IsRepo() && policy == VersionPolicy.Pinned)
                {
                    return Invalid("versionPolicy", "Bei Repo-Paketen wird die Version in der Paketkonfiguration festgelegt.");
                }

                var pinError = await ValidatePinAsync(db, assignment.PackageId, policy, request.PinnedVersionId, ct);
                if (pinError is not null)
                {
                    return Invalid("pinnedVersionId", pinError);
                }

                assignment.ChangePolicy(policy, request.PinnedVersionId);
                await db.SaveChangesAsync(ct);
                trigger.Request();
                return Results.NoContent();
            })
            .RequireAuthorization(Policies.Operator);

        assignments.MapDelete("/{id:guid}", async (Guid id, SoftwareDbContext db, DeploymentTrigger trigger, CancellationToken ct) =>
            {
                var deleted = await db.Assignments.Where(a => a.Id == id).ExecuteDeleteAsync(ct);
                trigger.Request();
                return deleted == 0 ? Results.NotFound() : Results.NoContent();
            })
            .RequireAuthorization(Policies.Operator);

        var installations = endpoints.MapGroup("/software/installations").WithTags("Software").RequireAuthorization();

        installations.MapGet("/", async (Guid packageId, SoftwareDbContext db, IDeviceDirectory directory, CancellationToken ct) =>
        {
            var package = await db.Packages.AsNoTracking().SingleOrDefaultAsync(p => p.Id == packageId, ct);
            if (package is null)
            {
                return Results.NotFound();
            }

            var rows = await db.Installations.AsNoTracking()
                .Where(i => i.PackageId == packageId)
                .OrderByDescending(i => i.UpdatedAt)
                .Take(500)
                .ToListAsync(ct);

            var versions = await db.Versions.AsNoTracking()
                .Where(v => v.PackageId == packageId)
                .ToDictionaryAsync(v => v.Id, v => v.Version, ct);
            var devices = await directory.GetManyAsync(rows.Select(i => i.DeviceId).ToList(), ct);

            return Results.Ok(rows.Select(i => new InstallationDto(
                i.Id, i.DeviceId,
                devices.TryGetValue(i.DeviceId, out var device) ? device.Name : null,
                i.PackageId, package.Name,
                versions.GetValueOrDefault(i.TargetVersionId),
                i.InstalledVersionId is { } installed ? versions.GetValueOrDefault(installed) : null,
                i.Status.ToString(), i.Attempts, i.Message, i.UpdatedAt)));
        });

        installations.MapPost("/{id:guid}/retry", async (Guid id, SoftwareDbContext db, DeploymentTrigger trigger, CancellationToken ct) =>
            {
                var installation = await db.Installations.SingleOrDefaultAsync(i => i.Id == id, ct);
                if (installation is null)
                {
                    return Results.NotFound();
                }

                installation.Retry(DateTimeOffset.UtcNow);
                await db.SaveChangesAsync(ct);
                trigger.Request();
                return Results.NoContent();
            })
            .RequireAuthorization(Policies.Operator);
    }

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static async Task<string?> ValidatePinAsync(
        SoftwareDbContext db, Guid packageId, VersionPolicy policy, Guid? pinnedVersionId, CancellationToken ct)
    {
        if (policy != VersionPolicy.Pinned)
        {
            return null;
        }

        if (pinnedVersionId is null)
        {
            return "Für eine feste Version muss eine Version gewählt werden.";
        }

        var exists = await db.Versions.AnyAsync(v => v.Id == pinnedVersionId && v.PackageId == packageId, ct);
        return exists ? null : "Die gewählte Version gehört nicht zu diesem Paket.";
    }
}
