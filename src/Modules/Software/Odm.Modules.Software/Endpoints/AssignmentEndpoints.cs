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

            var now = DateTimeOffset.UtcNow;
            return Results.Ok(rows.Select(a =>
            {
                var plan = RolloutPlan.Parse(a.RolloutJson);
                return new AssignmentDto(
                    a.Id, a.PackageId, a.TargetKind.ToString(), a.Target,
                    a.TargetKind == TargetKind.Device && Guid.TryParse(a.Target, out var id) && devices.TryGetValue(id, out var d) ? d.Name : null,
                    a.VersionPolicy.ToString(), a.PinnedVersionId, a.CreatedAt,
                    AssignmentTargets.ToDto(plan), AssignmentTargets.ToStatus(plan, a.CreatedAt, now), a.Intent.ToString());
            }));
        });

        assignments.MapPost("/", async (
                AssignmentRequest request, SoftwareDbContext db, IDeviceDirectory directory,
                DeploymentTrigger trigger, CancellationToken ct) =>
            {
                if (!Enum.TryParse<VersionPolicy>(request.VersionPolicy, true, out var policy) || !Enum.IsDefined(policy))
                {
                    return Invalid("versionPolicy", "Ungültige Versionsregel.");
                }

                if (!TryParseIntent(request.Intent, out var intent))
                {
                    return Invalid("intent", "Ungültige Absicht.");
                }

                var (kind, target, errorField, targetError) =
                    await AssignmentTargets.ParseAsync(request.TargetKind, request.Target, directory, ct);
                if (targetError is not null)
                {
                    return Invalid(errorField!, targetError);
                }

                var (rollout, rolloutError) = await AssignmentTargets.ParseRolloutAsync(request.Rollout, directory, ct);
                if (rolloutError is not null)
                {
                    return Invalid("rollout", rolloutError);
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

                var assignment = Assignment.Create(request.PackageId, kind, target, policy, request.PinnedVersionId, rollout, intent);
                db.Assignments.Add(assignment);
                await db.SaveChangesAsync(ct);
                trigger.Request();

                return Results.Created($"/api/software/assignments/{assignment.Id}",
                    new AssignmentDto(assignment.Id, assignment.PackageId, kind.ToString(), assignment.Target, null,
                        policy.ToString(), assignment.PinnedVersionId, assignment.CreatedAt,
                        AssignmentTargets.ToDto(rollout),
                        AssignmentTargets.ToStatus(rollout, assignment.CreatedAt, DateTimeOffset.UtcNow), intent.ToString()));
            })
            .RequireAuthorization(Policies.Operator);

        assignments.MapPut("/{id:guid}", async (
                Guid id, UpdateAssignmentRequest request, SoftwareDbContext db, IDeviceDirectory directory,
                DeploymentTrigger trigger, CancellationToken ct) =>
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

                if (!TryParseIntent(request.Intent ?? assignment.Intent.ToString(), out var newIntent))
                {
                    return Invalid("intent", "Ungültige Absicht.");
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

                var (rollout, rolloutError) = await AssignmentTargets.ParseRolloutAsync(request.Rollout, directory, ct);
                if (rolloutError is not null)
                {
                    return Invalid("rollout", rolloutError);
                }

                assignment.ChangePolicy(policy, request.PinnedVersionId);
                assignment.SetIntent(newIntent);
                assignment.SetRollout(rollout);
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
                i.Status.ToString(), i.Attempts, i.Message, i.UpdatedAt, i.Removal)));
        });

        installations.MapGet("/by-device", async (Guid deviceId, SoftwareDbContext db, IDeviceDirectory directory, CancellationToken ct) =>
        {
            var device = await directory.GetAsync(deviceId, ct);
            if (device is null)
            {
                return Results.NotFound();
            }

            var rows = await db.Installations.AsNoTracking()
                .Where(i => i.DeviceId == deviceId)
                .OrderBy(i => i.UpdatedAt)
                .Take(500)
                .ToListAsync(ct);

            var packageIds = rows.Select(i => i.PackageId).Distinct().ToList();
            var names = await db.Packages.AsNoTracking()
                .Where(p => packageIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name, ct);
            var versions = await db.Versions.AsNoTracking()
                .Where(v => packageIds.Contains(v.PackageId))
                .ToDictionaryAsync(v => v.Id, v => v.Version, ct);

            return Results.Ok(rows.Select(i => new InstallationDto(
                i.Id, i.DeviceId, device.Name,
                i.PackageId, names.GetValueOrDefault(i.PackageId, "?"),
                versions.GetValueOrDefault(i.TargetVersionId),
                i.InstalledVersionId is { } installed ? versions.GetValueOrDefault(installed) : null,
                i.Status.ToString(), i.Attempts, i.Message, i.UpdatedAt, i.Removal)));
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

    private static bool TryParseIntent(string? value, out AssignmentIntent intent)
    {
        intent = AssignmentIntent.Install;
        return value is null ||
               (Enum.TryParse(value, true, out intent) && Enum.IsDefined(intent));
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
