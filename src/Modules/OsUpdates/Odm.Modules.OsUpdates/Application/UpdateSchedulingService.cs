using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Odm.Modules.OsUpdates.Domain;
using Odm.Modules.OsUpdates.Infrastructure;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.OsUpdates.Application;

public enum RunNowResult
{
    Started,
    NoPolicy,
    NoAgent
}

/// <summary>
/// Plant Update-Läufe: Ist ein Wartungsfenster offen und gilt für das Gerät eine Richtlinie,
/// wird ein "os-update"-Auftrag an den Agent erteilt (je Gerät und Fenster höchstens einmal).
/// </summary>
public sealed class UpdateSchedulingService(
    OsUpdatesDbContext db,
    IDeviceDirectory directory,
    IJobQueue jobs,
    ILogger<UpdateSchedulingService> logger)
{
    private const int TimeoutMinutes = 240;
    private const int ManualWindowMinutes = 60;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task RunScheduledAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var windows = await db.Windows.AsNoTracking().ToDictionaryAsync(w => w.Id, ct);
        if (windows.Count == 0)
        {
            return; // ohne Wartungsfenster wird nie automatisch aktualisiert
        }

        var policies = await db.Policies.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
        var assignments = await db.Assignments.AsNoTracking().OrderBy(a => a.CreatedAt).ToListAsync(ct);
        var groupCache = new Dictionary<Guid, IReadOnlyList<DeviceRef>>();

        // Richtlinie je Gerät: direkte Zuweisung vor Gruppe; bei mehreren Gruppen gewinnt die älteste Zuweisung.
        var policyOf = new Dictionary<Guid, (Guid PolicyId, int Priority)>();
        foreach (var a in assignments.Where(a => a.Scope == AssignmentScope.Policy && policies.ContainsKey(a.ParentId)))
        {
            foreach (var device in await ResolveAsync(a, groupCache, ct))
            {
                var priority = (int)a.TargetKind;
                if (!policyOf.TryGetValue(device.Id, out var current) || priority < current.Priority)
                {
                    policyOf[device.Id] = (a.ParentId, priority);
                }
            }
        }

        // Fenster je Gerät: direkt zugewiesene Fenster ersetzen die der Gruppen; mehrere Gruppenfenster addieren sich.
        var direct = new Dictionary<Guid, HashSet<Guid>>();
        var viaGroup = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var a in assignments.Where(a => a.Scope == AssignmentScope.Window && windows.ContainsKey(a.ParentId)))
        {
            var map = a.TargetKind == TargetKind.Device ? direct : viaGroup;
            foreach (var device in await ResolveAsync(a, groupCache, ct))
            {
                if (!map.TryGetValue(device.Id, out var set))
                {
                    map[device.Id] = set = [];
                }

                set.Add(a.ParentId);
            }
        }

        var since = now.AddDays(-2);
        var existing = (await db.Runs.AsNoTracking()
                .Where(r => r.OccurrenceStart >= since)
                .Select(r => new { r.DeviceId, r.WindowId, r.OccurrenceStart })
                .ToListAsync(ct))
            .Select(r => (r.DeviceId, r.WindowId, r.OccurrenceStart))
            .ToHashSet();

        var started = 0;
        foreach (var (deviceId, assigned) in policyOf)
        {
            var effective = direct.TryGetValue(deviceId, out var own) ? own
                : viaGroup.TryGetValue(deviceId, out var inherited) ? inherited
                : null;
            if (effective is null)
            {
                continue;
            }

            var policy = policies[assigned.PolicyId];
            foreach (var windowId in effective)
            {
                if (!windows[windowId].TryGetOccurrence(now, out var start, out var end) ||
                    existing.Contains((deviceId, windowId, start)))
                {
                    continue;
                }

                var jobId = await EnqueueAsync(deviceId, policy, end, ct);
                if (jobId is null)
                {
                    break; // kein Agent registriert
                }

                db.Runs.Add(UpdateRun.Create(deviceId, windowId, start, jobId.Value, now));
                await db.SaveChangesAsync(ct);
                existing.Add((deviceId, windowId, start));
                started++;
                break; // ein Lauf je Gerät und Durchgang
            }
        }

        if (started > 0)
        {
            logger.LogInformation("Wartungsfenster: {Count} Update-Läufe gestartet.", started);
        }
    }

    /// <summary>Startet sofort einen Lauf (ausserhalb der Fenster), z. B. zum Testen oder für dringende Patches.</summary>
    public async Task<RunNowResult> RunNowAsync(Guid deviceId, CancellationToken ct)
    {
        var policy = await FindPolicyForDeviceAsync(deviceId, ct);
        if (policy is null)
        {
            return RunNowResult.NoPolicy;
        }

        var now = DateTimeOffset.UtcNow;
        var jobId = await EnqueueAsync(deviceId, policy, now.AddMinutes(ManualWindowMinutes), ct);
        if (jobId is null)
        {
            return RunNowResult.NoAgent;
        }

        db.Runs.Add(UpdateRun.Create(deviceId, Guid.Empty, now, jobId.Value, now));
        await db.SaveChangesAsync(ct);
        return RunNowResult.Started;
    }

    private async Task<UpdatePolicy?> FindPolicyForDeviceAsync(Guid deviceId, CancellationToken ct)
    {
        var assignments = await db.Assignments.AsNoTracking()
            .Where(a => a.Scope == AssignmentScope.Policy)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

        var deviceTarget = deviceId.ToString("D");
        var match = assignments.FirstOrDefault(a => a.TargetKind == TargetKind.Device && a.Target == deviceTarget);

        if (match is null)
        {
            foreach (var a in assignments.Where(a => a.TargetKind == TargetKind.Group))
            {
                var members = await directory.ResolveGroupAsync(Guid.Parse(a.Target), ct);
                if (members.Any(m => m.Id == deviceId))
                {
                    match = a;
                    break;
                }
            }
        }

        return match is null ? null : await db.Policies.AsNoTracking().SingleOrDefaultAsync(p => p.Id == match.ParentId, ct);
    }

    private Task<Guid?> EnqueueAsync(Guid deviceId, UpdatePolicy policy, DateTimeOffset windowEnd, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(
            new OsUpdatePayload(policy.Mode.ToString(), policy.Reboot == RebootBehavior.IfRequired, windowEnd, TimeoutMinutes),
            Json);
        return jobs.EnqueueAsync(deviceId, AgentJobTypes.OsUpdate, payload, ct);
    }

    private async Task<IReadOnlyList<DeviceRef>> ResolveAsync(
        OsAssignment assignment, Dictionary<Guid, IReadOnlyList<DeviceRef>> groupCache, CancellationToken ct)
    {
        if (!Guid.TryParse(assignment.Target, out var id))
        {
            return [];
        }

        if (assignment.TargetKind == TargetKind.Device)
        {
            return await directory.GetAsync(id, ct) is { } device ? [device] : [];
        }

        if (!groupCache.TryGetValue(id, out var members))
        {
            groupCache[id] = members = await directory.ResolveGroupAsync(id, ct);
        }

        return members;
    }
}
