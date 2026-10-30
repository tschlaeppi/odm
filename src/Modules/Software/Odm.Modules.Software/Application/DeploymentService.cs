using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Odm.Modules.Software.Domain;
using Odm.Modules.Software.Infrastructure;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.Software.Application;

/// <summary>
/// Gleicht Soll (Zuweisungen) und Ist (Installationen) ab und erzeugt Agent-Aufträge.
/// Läuft regelmässig, nicht nur bei Änderungen: so werden auch neue Gruppenmitglieder,
/// neue Paketversionen und automatisch aktualisierte Repo-Pakete erfasst.
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

        // 1. Soll-Zustand: (Gerät, Paket) -> Version, bei mehreren Zuweisungen gewinnt die spezifischere.
        var desired = new Dictionary<(Guid Device, Guid Package), (Guid Version, int Priority)>();
        var assignments = await db.Assignments.AsNoTracking().OrderBy(a => a.CreatedAt).ToListAsync(ct);

        foreach (var assignment in assignments)
        {
            if (!packages.TryGetValue(assignment.PackageId, out var package))
            {
                continue;
            }

            Guid versionId;
            if (package.Kind == PackageKind.Psadt && assignment.VersionPolicy == VersionPolicy.Pinned)
            {
                if (assignment.PinnedVersionId is not { } pinned ||
                    !versionsById.TryGetValue(pinned, out var pinnedVersion) ||
                    pinnedVersion.PackageId != assignment.PackageId)
                {
                    continue;
                }

                versionId = pinned;
            }
            else if (!latestByPackage.TryGetValue(assignment.PackageId, out versionId))
            {
                continue; // Paket hat noch keine Version bzw. Revision
            }

            foreach (var device in await ResolveAsync(assignment, ct))
            {
                if (!DistroFamily.Applies(package.Kind, device))
                {
                    continue;
                }

                var key = (device.Id, assignment.PackageId);
                var priority = (int)assignment.TargetKind;
                if (!desired.TryGetValue(key, out var current) || priority < current.Priority)
                {
                    desired[key] = (versionId, priority);
                }
            }
        }

        // 2. Installationen anlegen bzw. neu bewerten
        var installations = await db.Installations.ToListAsync(ct);
        var byKey = installations.ToDictionary(i => (i.DeviceId, i.PackageId));

        foreach (var (key, want) in desired)
        {
            if (byKey.TryGetValue(key, out var installation))
            {
                // Repo-Pakete ohne feste Version regelmässig vom Paketmanager aktualisieren lassen.
                var refresh = packages[key.Package].Kind.IsRepo() && specs[key.Package].AutoUpdate ? repoRefresh : null;
                installation.Evaluate(want.Version, now, refresh);
            }
            else
            {
                db.Installations.Add(Installation.Create(key.Device, key.Package, want.Version, now));
            }
        }

        await db.SaveChangesAsync(ct);

        // 3. Offene Installationen als Aufträge einreihen
        var pending = db.Installations.Local
            .Where(i => i.Status == InstallStatus.Pending && desired.ContainsKey((i.DeviceId, i.PackageId)))
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
            var version = versionEntities[installation.TargetVersionId];
            var package = packages[installation.PackageId];
            var (jobType, payload) = BuildJob(package, specs[package.Id], version);

            var jobId = await jobs.EnqueueAsync(installation.DeviceId, jobType, payload, ct);
            if (jobId is null)
            {
                continue; // Gerät hat (noch) keinen Agent; nächster Lauf versucht es erneut
            }

            installation.MarkQueued(jobId.Value, now);
            await db.SaveChangesAsync(ct); // sofort speichern: der Auftrag existiert bereits
        }

        logger.LogInformation("Abgleich: {Count} Installationsaufträge eingereiht.", pending.Count(i => i.Status == InstallStatus.Running));
    }

    /// <summary>
    /// Baut den Auftrag. Argumente enthalten nur Verweise {{secret:NAME}}; die Werte holt der Agent erst bei der
    /// Ausführung ab, damit sie nie in der Auftragstabelle landen.
    /// </summary>
    private static (string Type, string Payload) BuildJob(SoftwarePackage package, PackageSpec current, PackageVersion version)
    {
        if (package.Kind == PackageKind.Psadt)
        {
            return (AgentJobTypes.InstallPsadt, JsonSerializer.Serialize(
                new InstallPsadtPayload(
                    version.Id, package.Name, version.Version, version.Sha256 ?? "", version.SizeBytes,
                    InstallTimeoutMinutes, current.Arguments),
                Json));
        }

        // Repo-Paket: die Einstellungen der Revision gelten, nicht die aktuell bearbeiteten.
        var spec = PackageSpec.Parse(version.SpecSnapshot);
        return (AgentJobTypes.InstallRepo, JsonSerializer.Serialize(
            new InstallRepoPayload(
                package.Kind.ToString(), spec.Identifier ?? "", spec.Version, spec.Scope, spec.Source, spec.Remote,
                spec.Channel, spec.Classic, spec.Arguments, InstallTimeoutMinutes),
            Json));
    }

    private async Task<IReadOnlyList<DeviceRef>> ResolveAsync(Assignment assignment, CancellationToken ct)
    {
        switch (assignment.TargetKind)
        {
            case TargetKind.Device:
                if (Guid.TryParse(assignment.Target, out var deviceId) && await directory.GetAsync(deviceId, ct) is { } device)
                {
                    return [device];
                }

                return [];
            case TargetKind.Group:
                return Guid.TryParse(assignment.Target, out var groupId)
                    ? await directory.ResolveGroupAsync(groupId, ct)
                    : [];
            case TargetKind.User:
                return await directory.ResolveUserAsync(assignment.Target, ct);
            default:
                return [];
        }
    }
}
