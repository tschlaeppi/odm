using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Odm.Modules.Agents.Domain;
using Odm.Modules.Agents.Infrastructure;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.Agents.Application;

public sealed class AgentService(
    AgentsDbContext db,
    IDeviceRegistry devices,
    IEnumerable<IJobResultListener> listeners,
    IOptions<AgentsOptions> options,
    ILogger<AgentService> logger)
{
    private const int MaxJobsPerHeartbeat = 10;
    private const int MaxResultsPerHeartbeat = 50;

    /// <summary>
    /// Ausgelieferte Aufträge, die der Agent weder als laufend noch als erledigt meldet,
    /// werden nach dieser Zeit erneut ausgeliefert (z. B. nach Absturz oder Neustart des Agents).
    /// </summary>
    private static readonly TimeSpan RedeliveryAfter = TimeSpan.FromMinutes(10);

    private int HeartbeatSeconds => Math.Clamp(options.Value.HeartbeatSeconds, 10, 3600);

    /// <summary>Registriert einen Agent. Gibt null zurück, wenn das Token ungültig, abgelaufen oder verbraucht ist.</summary>
    public async Task<RegisterResponse?> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var hash = Secrets.Hash(request.Token);

        // Token atomar "verbrauchen": bei gleichzeitigen Anfragen gewinnt genau eine.
        var claimed = await db.Tokens
            .Where(t => t.TokenHash == hash && t.UsedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), ct);
        if (claimed != 1)
        {
            return null;
        }

        var deviceId = await devices.RegisterAsync(
            new DeviceReport(request.Hostname, request.OsFamily, request.OsName, request.OsVersion, request.AgentVersion),
            ct);

        // Eine Neuinstallation ersetzt frühere Zugangsdaten desselben Geräts.
        await db.Credentials
            .Where(c => c.DeviceId == deviceId && c.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.RevokedAt, now), ct);

        var (credential, secret) = AgentCredential.Issue(deviceId);
        db.Credentials.Add(credential);
        await db.SaveChangesAsync(ct);

        return new RegisterResponse(credential.Id, secret, deviceId, HeartbeatSeconds);
    }

    public async Task<HeartbeatResponse> HeartbeatAsync(
        Guid agentId, Guid deviceId, HeartbeatRequest request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // 1. Ergebnisse früherer Aufträge übernehmen (nur für Aufträge dieses Geräts)
        var completed = new List<AgentJob>();
        foreach (var result in (request.Results ?? []).Take(MaxResultsPerHeartbeat))
        {
            var job = await db.Jobs.SingleOrDefaultAsync(j => j.Id == result.JobId && j.DeviceId == deviceId, ct);
            if (job is not null && job.Complete(result.Success, result.Output, now))
            {
                completed.Add(job);
            }
        }

        // 2. Laufende Aufträge: "Lease" verlängern, damit sie nicht erneut ausgeliefert werden
        var running = (request.RunningJobs ?? []).Take(MaxJobsPerHeartbeat * 5).ToList();
        if (running.Count > 0)
        {
            await db.Jobs
                .Where(j => running.Contains(j.Id) && j.DeviceId == deviceId && j.Status == JobStatus.Delivered)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.DeliveredAt, now), ct);
        }

        // 3. Geräte-Informationen aktualisieren
        await devices.ReportAsync(
            deviceId,
            new DeviceReport("", request.OsFamily, request.OsName, request.OsVersion, request.AgentVersion),
            ct);

        await db.Credentials
            .Where(c => c.Id == agentId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastSeenAt, now), ct);

        // 4. Fällige Aufträge ausliefern
        var cutoff = now - RedeliveryAfter;
        var due = await db.Jobs
            .Where(j => j.DeviceId == deviceId &&
                        (j.Status == JobStatus.Pending || (j.Status == JobStatus.Delivered && j.DeliveredAt < cutoff)))
            .OrderBy(j => j.CreatedAt)
            .Take(MaxJobsPerHeartbeat)
            .ToListAsync(ct);

        foreach (var job in due)
        {
            job.MarkDelivered(now);
        }

        await db.SaveChangesAsync(ct);

        // 5. Andere Module informieren (erst nachdem der Zustand gespeichert ist)
        foreach (var job in completed)
        {
            var completion = new JobCompletion(job.Id, job.DeviceId, job.Type, job.Status == JobStatus.Succeeded, job.Output);
            foreach (var listener in listeners)
            {
                try
                {
                    await listener.OnJobCompletedAsync(completion, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Verarbeitung des Auftragsergebnisses {JobId} ist fehlgeschlagen.", job.Id);
                }
            }
        }

        return new HeartbeatResponse(
            due.Select(j => new AgentJobDto(j.Id, j.Type, j.Payload)).ToList(),
            HeartbeatSeconds);
    }
}
