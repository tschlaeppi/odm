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

/// <summary>Softwareprofile (geordnete Paketlisten) und ihre Zuweisungen.</summary>
internal static class ProfileEndpoints
{
    private const int MaxItems = 50;

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var profiles = endpoints.MapGroup("/software/profiles").WithTags("Software").RequireAuthorization();

        profiles.MapGet("/", async (SoftwareDbContext db, CancellationToken ct) =>
        {
            var counts = await db.ProfileItems.AsNoTracking()
                .GroupBy(i => i.ProfileId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var rows = await db.Profiles.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);
            return Results.Ok(rows.Select(p =>
                new ProfileDto(p.Id, p.Name, p.Description, counts.GetValueOrDefault(p.Id), p.CreatedAt)));
        });

        profiles.MapGet("/{id:guid}", async (Guid id, SoftwareDbContext db, CancellationToken ct) =>
        {
            var profile = await db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
            return profile is null ? Results.NotFound() : Results.Ok(await ToDetailAsync(db, profile, ct));
        });

        profiles.MapPost("/", async (CreateProfileRequest request, SoftwareDbContext db, DeploymentTrigger trigger, CancellationToken ct) =>
            {
                var name = request.Name?.Trim() ?? "";
                if (name.Length is 0 or > 128)
                {
                    return Invalid("name", "Name is required (at most 128 characters).");
                }

                if (request.Description is { Length: > 1024 })
                {
                    return Invalid("description", "Description is too long.");
                }

                if (await db.Profiles.AnyAsync(p => p.Name == name, ct))
                {
                    return Results.Conflict();
                }

                var profile = SoftwareProfile.Create(name, request.Description);
                var itemError = await BuildItemsAsync(db, profile.Id, request.Items, ct);
                if (itemError.Error is not null)
                {
                    return Invalid("items", itemError.Error);
                }

                db.Profiles.Add(profile);
                db.ProfileItems.AddRange(itemError.Items);
                await db.SaveChangesAsync(ct);
                trigger.Request();
                return Results.Created($"/api/software/profiles/{profile.Id}", await ToDetailAsync(db, profile, ct));
            })
            .RequireAuthorization(Policies.Operator);

        profiles.MapPut("/{id:guid}", async (
                Guid id, UpdateProfileRequest request, SoftwareDbContext db, DeploymentTrigger trigger, CancellationToken ct) =>
            {
                var profile = await db.Profiles.SingleOrDefaultAsync(p => p.Id == id, ct);
                if (profile is null)
                {
                    return Results.NotFound();
                }

                if (request.Description is { Length: > 1024 })
                {
                    return Invalid("description", "Description is too long.");
                }

                var built = await BuildItemsAsync(db, id, request.Items, ct);
                if (built.Error is not null)
                {
                    return Invalid("items", built.Error);
                }

                // Löschen und Neuanlegen in einer Transaktion: die Reihenfolge wird als Ganzes ersetzt.
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                profile.Update(request.Description);
                await db.ProfileItems.Where(i => i.ProfileId == id).ExecuteDeleteAsync(ct);
                db.ProfileItems.AddRange(built.Items);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                trigger.Request();
                return Results.Ok(await ToDetailAsync(db, profile, ct));
            })
            .RequireAuthorization(Policies.Operator);

        profiles.MapDelete("/{id:guid}", async (Guid id, SoftwareDbContext db, DeploymentTrigger trigger, CancellationToken ct) =>
            {
                var deleted = await db.Profiles.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
                trigger.Request();
                return deleted == 0 ? Results.NotFound() : Results.NoContent();
            })
            .RequireAuthorization(Policies.Operator);

        var assignments = endpoints.MapGroup("/software/profile-assignments").WithTags("Software").RequireAuthorization();

        assignments.MapGet("/", async (Guid profileId, SoftwareDbContext db, IDeviceDirectory directory, CancellationToken ct) =>
        {
            var rows = await db.ProfileAssignments.AsNoTracking()
                .Where(a => a.ProfileId == profileId)
                .OrderBy(a => a.CreatedAt)
                .ToListAsync(ct);

            var deviceIds = rows.Where(a => a.TargetKind == TargetKind.Device && Guid.TryParse(a.Target, out _))
                .Select(a => Guid.Parse(a.Target)).ToList();
            var devices = await directory.GetManyAsync(deviceIds, ct);

            var now = DateTimeOffset.UtcNow;
            return Results.Ok(rows.Select(a => ToDto(
                a,
                a.TargetKind == TargetKind.Device && Guid.TryParse(a.Target, out var id) && devices.TryGetValue(id, out var d) ? d.Name : null,
                now)));
        });

        assignments.MapPost("/", async (
                ProfileAssignmentRequest request, SoftwareDbContext db, IDeviceDirectory directory,
                DeploymentTrigger trigger, CancellationToken ct) =>
            {
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

                if (!await db.Profiles.AnyAsync(p => p.Id == request.ProfileId, ct))
                {
                    return Results.NotFound();
                }

                var normalized = target.ToLowerInvariant();
                if (await db.ProfileAssignments.AnyAsync(
                        a => a.ProfileId == request.ProfileId && a.TargetKind == kind && a.Target == normalized, ct))
                {
                    return Results.Conflict();
                }

                var assignment = ProfileAssignment.Create(request.ProfileId, kind, target, rollout);
                db.ProfileAssignments.Add(assignment);
                await db.SaveChangesAsync(ct);
                trigger.Request();
                return Results.Created($"/api/software/profile-assignments/{assignment.Id}",
                    ToDto(assignment, null, DateTimeOffset.UtcNow));
            })
            .RequireAuthorization(Policies.Operator);

        assignments.MapPut("/{id:guid}", async (
                Guid id, UpdateProfileAssignmentRequest request, SoftwareDbContext db, IDeviceDirectory directory,
                DeploymentTrigger trigger, CancellationToken ct) =>
            {
                var assignment = await db.ProfileAssignments.SingleOrDefaultAsync(a => a.Id == id, ct);
                if (assignment is null)
                {
                    return Results.NotFound();
                }

                var (rollout, rolloutError) = await AssignmentTargets.ParseRolloutAsync(request.Rollout, directory, ct);
                if (rolloutError is not null)
                {
                    return Invalid("rollout", rolloutError);
                }

                assignment.SetRollout(rollout);
                await db.SaveChangesAsync(ct);
                trigger.Request();
                return Results.NoContent();
            })
            .RequireAuthorization(Policies.Operator);

        assignments.MapDelete("/{id:guid}", async (Guid id, SoftwareDbContext db, DeploymentTrigger trigger, CancellationToken ct) =>
            {
                var deleted = await db.ProfileAssignments.Where(a => a.Id == id).ExecuteDeleteAsync(ct);
                trigger.Request();
                return deleted == 0 ? Results.NotFound() : Results.NoContent();
            })
            .RequireAuthorization(Policies.Operator);
    }

    private static ProfileAssignmentDto ToDto(ProfileAssignment a, string? targetName, DateTimeOffset now)
    {
        var plan = RolloutPlan.Parse(a.RolloutJson);
        return new ProfileAssignmentDto(
            a.Id, a.ProfileId, a.TargetKind.ToString(), a.Target, targetName, a.CreatedAt,
            AssignmentTargets.ToDto(plan), AssignmentTargets.ToStatus(plan, a.CreatedAt, now));
    }

    private static async Task<ProfileDetailDto> ToDetailAsync(SoftwareDbContext db, SoftwareProfile profile, CancellationToken ct)
    {
        var items = await db.ProfileItems.AsNoTracking()
            .Where(i => i.ProfileId == profile.Id)
            .OrderBy(i => i.Position)
            .ToListAsync(ct);
        var packageIds = items.Select(i => i.PackageId).ToList();
        var packages = await db.Packages.AsNoTracking()
            .Where(p => packageIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        return new ProfileDetailDto(profile.Id, profile.Name, profile.Description, profile.CreatedAt,
            items.Where(i => packages.ContainsKey(i.PackageId))
                .Select(i => new ProfileItemDto(
                    i.PackageId, packages[i.PackageId].Name, packages[i.PackageId].Kind.ToString(), i.Position,
                    i.VersionPolicy.ToString(), i.PinnedVersionId))
                .ToList());
    }

    /// <summary>Prüft die Einträge und legt sie in der angegebenen Reihenfolge an.</summary>
    private static async Task<(IReadOnlyList<ProfileItem> Items, string? Error)> BuildItemsAsync(
        SoftwareDbContext db, Guid profileId, IReadOnlyList<ProfileItemRequest>? requested, CancellationToken ct)
    {
        if (requested is null or { Count: 0 })
        {
            return ([], "Ein Profil braucht mindestens ein Paket.");
        }

        if (requested.Count > MaxItems)
        {
            return ([], $"A profile may contain at most {MaxItems} packages.");
        }

        if (requested.Select(r => r.PackageId).Distinct().Count() != requested.Count)
        {
            return ([], "Jedes Paket darf nur einmal vorkommen.");
        }

        var ids = requested.Select(r => r.PackageId).ToList();
        var packages = await db.Packages.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var items = new List<ProfileItem>();

        foreach (var (request, position) in requested.Select((r, i) => (r, i)))
        {
            if (!packages.TryGetValue(request.PackageId, out var package))
            {
                return ([], "A package was not found.");
            }

            var policy = VersionPolicy.Latest;
            if (!package.Kind.IsRepo() && !string.IsNullOrWhiteSpace(request.VersionPolicy) &&
                (!Enum.TryParse(request.VersionPolicy, true, out policy) || !Enum.IsDefined(policy)))
            {
                return ([], "Invalid version policy.");
            }

            if (policy == VersionPolicy.Pinned &&
                (request.PinnedVersionId is null ||
                 !await db.Versions.AnyAsync(v => v.Id == request.PinnedVersionId && v.PackageId == package.Id, ct)))
            {
                return ([], $"A valid pinned version is missing for '{package.Name}'.");
            }

            items.Add(ProfileItem.Create(profileId, package.Id, position, policy, request.PinnedVersionId));
        }

        return (items, null);
    }

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
