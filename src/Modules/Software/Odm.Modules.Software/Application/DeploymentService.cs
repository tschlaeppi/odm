using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Odm.Modules.Software.Domain;
using Odm.Modules.Software.Infrastructure;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.Software.Application;

/// <summary>
/// Reconciles desired state (assignments) and actual state (installations) and creates agent jobs.
/// Runs regularly, not only on changes: this also catches new group members,
/// new package versions and automatically updated repo packages.
/// </summary>
public sealed class DeploymentService(
    SoftwareDbContext db,
    IDeviceDirectory directory,
    IJobQueue jobs,
    IOptions<SoftwareOptions> options,
    ILogger<DeploymentService> logger)
{
    private const int MaxEnqueuePerRun = 500;
    private const int InstallTimeoutMinutes = 60;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record VersionInfo(Guid Id, Guid PackageId, DateTimeOffset UploadedAt);

    private sealed record Desired(Guid Version, int Priority, IReadOnlyList<Guid> Prerequisites, bool Uninstall = false);

    private sealed record SourceItem(
        Guid PackageId, VersionPolicy Policy, Guid? PinnedVersionId, AssignmentIntent Intent = AssignmentIntent.Install);

    /// <summary>An assignment as a source of the desired state; for profiles several entries in installation order.</summary>
    private sealed record Source(
        Guid Id, TargetKind Kind, string Target, DateTimeOffset Start, RolloutPlan? Rollout, bool FromProfile,
        IReadOnlyList<SourceItem> Items);

    private async Task<List<Source>> LoadSourcesAsync(CancellationToken ct)
    {
        var sources = new List<Source>();

        foreach (var a in await db.Assignments.AsNoTracking().OrderBy(o => o.CreatedAt).ToListAsync(ct))
        {
            sources.Add(new Source(a.Id, a.TargetKind, a.Target, a.CreatedAt, RolloutPlan.Parse(a.RolloutJson), false,
                [new SourceItem(a.PackageId, a.VersionPolicy, a.PinnedVersionId, a.Intent)]));
        }

        var profileItems = (await db.ProfileItems.AsNoTracking().OrderBy(i => i.Position).ToListAsync(ct))
            .GroupBy(i => i.ProfileId)
            .ToDictionary(g => g.Key, g => g.Select(i => new SourceItem(i.PackageId, i.VersionPolicy, i.PinnedVersionId)).ToList());

        foreach (var a in await db.ProfileAssignments.AsNoTracking().OrderBy(o => o.CreatedAt).ToListAsync(ct))
        {
            if (profileItems.TryGetValue(a.ProfileId, out var items))
            {
                sources.Add(new Source(a.Id, a.TargetKind, a.Target, a.CreatedAt, RolloutPlan.Parse(a.RolloutJson), true, items));
            }
        }

        return sources;
    }

    /// <summary>Desired version: pinned version (PSADT only) or the latest; null if there is none.</summary>
    private static Guid? ResolveVersion(
        SoftwarePackage package, VersionPolicy policy, Guid? pinnedVersionId,
        IReadOnlyDictionary<Guid, VersionInfo> versionsById, IReadOnlyDictionary<Guid, Guid> latestByPackage)
    {
        if (package.Kind == PackageKind.Psadt && policy == VersionPolicy.Pinned)
        {
            return pinnedVersionId is { } pinned &&
                   versionsById.TryGetValue(pinned, out var version) &&
                   version.PackageId == package.Id
                ? pinned
                : null;
        }

        return latestByPackage.TryGetValue(package.Id, out var latest) ? latest : null;
    }

    public async Task ReconcileAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var refreshHours = options.Value.RepoRefreshHours;
        TimeSpan? repoRefresh = refreshHours > 0 ? TimeSpan.FromHours(refreshHours) : null;

        var packages = await db.Packages.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
        var specs = packages.Values.ToDictionary(p => p.Id, p => p.Spec);
        var versions = await db.Versions.AsNoTracking()
            .Select(v => new VersionInfo(v.Id, v.PackageId, v.UploadedAt))
            .ToListAsync(ct);
        var versionsById = versions.ToDictionary(v => v.Id);
        var latestByPackage = versions
            .GroupBy(v => v.PackageId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.UploadedAt).First().Id);

        // Delete successfully uninstalled entries first (and save) so that a re-assignment causes no key conflict.
        var allInstallations = await db.Installations.ToListAsync(ct);
        var removed = allInstallations.Where(i => i.Status == InstallStatus.Removed).ToList();
        if (removed.Count > 0)
        {
            db.Installations.RemoveRange(removed);
            await db.SaveChangesAsync(ct);
        }

        var installations = allInstallations.Except(removed).ToList();
        var byKey = installations.ToDictionary(i => (i.DeviceId, i.PackageId));

        // 1. Sources of the desired state: single package assignments and profile assignments (with order).
        var sources = await LoadSourcesAsync(ct);

        var groupMembers = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var groupId in sources.Where(s => s.Rollout is { Mode: RolloutMode.Groups })
                     .SelectMany(s => s.Rollout!.Stages).Select(s => s.GroupId!.Value).Distinct())
        {
            groupMembers[groupId] = (await directory.ResolveGroupAsync(groupId, ct)).Select(d => d.Id).ToHashSet();
        }

        bool IsMember(Guid groupId, Guid deviceId) => groupMembers.TryGetValue(groupId, out var m) && m.Contains(deviceId);

        // (device, package) -> version; with several sources the more specific wins (device before user before group,
        // within that the single package assignment before the profile).
        var desired = new Dictionary<(Guid Device, Guid Package), Desired>();

        foreach (var source in sources)
        {
            var devices = await ResolveAsync(source.Kind, source.Target, ct);
            var elapsed = now - source.Start;
            var priority = (int)source.Kind * 2 + (source.FromProfile ? 1 : 0);
            var earlier = new Dictionary<Guid, List<Guid>>();

            foreach (var item in source.Items)
            {
                var uninstall = item.Intent == AssignmentIntent.Uninstall;
                if (!packages.TryGetValue(item.PackageId, out var package))
                {
                    continue; // package deleted
                }

                // When uninstalling, the installed version counts, not the desired one.
                var resolved = uninstall
                    ? Guid.Empty
                    : ResolveVersion(package, item.Policy, item.PinnedVersionId, versionsById, latestByPackage);
                if (resolved is not { } versionId)
                {
                    continue; // no version/revision or pinned version invalid
                }

                foreach (var device in devices)
                {
                    if (!DistroFamily.Applies(package.Kind, device))
                    {
                        continue;
                    }

                    var key = (device.Id, item.PackageId);

                    if (uninstall && !byKey.ContainsKey(key))
                    {
                        continue; // nothing installed by ODM to remove
                    }

                    // Tranches only apply to new installations; whoever already has the package is not held back.
                    var allowed = source.Rollout is null ||
                                  byKey.ContainsKey(key) ||
                                  source.Rollout.IsReleased(source.Id, device.Id, elapsed, IsMember);
                    if (!allowed)
                    {
                        continue;
                    }

                    var prerequisites = source.FromProfile && earlier.TryGetValue(device.Id, out var before)
                        ? before.ToList()
                        : [];

                    // At equal rank "install" wins over "uninstall".
                    if (!desired.TryGetValue(key, out var current) || priority < current.Priority ||
                        (priority == current.Priority && current.Uninstall && !uninstall))
                    {
                        desired[key] = new Desired(versionId, priority, prerequisites, uninstall);
                    }

                    if (source.FromProfile)
                    {
                        if (!earlier.TryGetValue(device.Id, out var list))
                        {
                            earlier[device.Id] = list = [];
                        }

                        list.Add(item.PackageId);
                    }
                }
            }
        }

        // 2. Installationen anlegen bzw. neu bewerten
        foreach (var (key, want) in desired)
        {
            if (byKey.TryGetValue(key, out var installation))
            {
                if (want.Uninstall)
                {
                    if (!installation.EvaluateRemoval(now))
                    {
                        db.Installations.Remove(installation); // war nie installiert: nichts zu entfernen
                    }

                    continue;
                }

                // Have the package manager update repo packages without a pinned version regularly.
                var refresh = packages[key.Package].Kind.IsRepo() && specs[key.Package].AutoUpdate ? repoRefresh : null;
                installation.Evaluate(want.Version, now, refresh);
            }
            else
            {
                db.Installations.Add(Installation.Create(key.Device, key.Package, want.Version, now));
            }
        }

        await db.SaveChangesAsync(ct);

        // 3. Queue open installations as jobs
        // Profiles: a package waits until all packages before it on the same device are installed.
        var statusByKey = db.Installations.Local.ToDictionary(i => (i.DeviceId, i.PackageId), i => i.Status);
        var pending = db.Installations.Local
            .Where(i => i.Status == InstallStatus.Pending &&
                        desired.TryGetValue((i.DeviceId, i.PackageId), out var want) &&
                        want.Prerequisites.All(p => statusByKey.GetValueOrDefault((i.DeviceId, p)) == InstallStatus.Installed))
            .Take(MaxEnqueuePerRun)
            .ToList();
        if (pending.Count == 0)
        {
            return;
        }

        var neededIds = pending.Select(i => i.TargetVersionId).Distinct().ToList();
        var versionEntities = await db.Versions.AsNoTracking()
            .Where(v => neededIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, ct);

        foreach (var installation in pending)
        {
            if (!versionEntities.TryGetValue(installation.TargetVersionId, out var version))
            {
                continue; // version was deleted
            }

            var package = packages[installation.PackageId];
            var (jobType, payload) = BuildJob(package, specs[package.Id], version, installation.Removal);

            var jobId = await jobs.EnqueueAsync(installation.DeviceId, jobType, payload, ct);
            if (jobId is null)
            {
                continue; // device has no agent (yet); the next run tries again
            }

            installation.MarkQueued(jobId.Value, now);
            await db.SaveChangesAsync(ct); // save immediately: the job already exists
        }

        logger.LogInformation("Reconciliation: {Count} installation jobs queued.", pending.Count(i => i.Status == InstallStatus.Running));
    }

    /// <summary>
    /// Builds the job. Arguments contain only references {{secret:NAME}}; the agent fetches the values only at
    /// execution time so that they never end up in the job table.
    /// </summary>
    private static (string Type, string Payload) BuildJob(
        SoftwarePackage package, PackageSpec current, PackageVersion version, bool uninstall)
    {
        if (package.Kind == PackageKind.Psadt)
        {
            return (AgentJobTypes.InstallPsadt, JsonSerializer.Serialize(
                new InstallPsadtPayload(
                    version.Id, package.Name, version.Version, version.Sha256 ?? "", version.SizeBytes,
                    InstallTimeoutMinutes, current.Arguments, uninstall),
                Json));
        }

        // Repo package: the settings of the revision apply, not the ones currently being edited.
        var spec = PackageSpec.Parse(version.SpecSnapshot);
        return (AgentJobTypes.InstallRepo, JsonSerializer.Serialize(
            new InstallRepoPayload(
                package.Kind.ToString(), spec.Identifier ?? "", spec.Version, spec.Scope, spec.Source, spec.Remote,
                spec.Channel, spec.Classic, spec.Arguments, InstallTimeoutMinutes, uninstall),
            Json));
    }

    private async Task<IReadOnlyList<DeviceRef>> ResolveAsync(TargetKind kind, string target, CancellationToken ct)
    {
        switch (kind)
        {
            case TargetKind.Device:
                if (Guid.TryParse(target, out var deviceId) && await directory.GetAsync(deviceId, ct) is { } device)
                {
                    return [device];
                }

                return [];
            case TargetKind.Group:
                return Guid.TryParse(target, out var groupId)
                    ? await directory.ResolveGroupAsync(groupId, ct)
                    : [];
            case TargetKind.User:
                return await directory.ResolveUserAsync(target, ct);
            default:
                return [];
        }
    }
}
