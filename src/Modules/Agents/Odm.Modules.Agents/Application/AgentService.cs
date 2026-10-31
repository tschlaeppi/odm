using System.Security.Cryptography;
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
    AgentPackageCatalog packages,
    AgentCertificateAuthority authority,
    ILogger<AgentService> logger)
{
    private const int MaxJobsPerHeartbeat = 10;
    private const int MaxResultsPerHeartbeat = 50;

    /// <summary>
    /// Delivered jobs that the agent reports neither as running nor as finished
    /// are delivered again after this time (e.g. after an agent crash or restart).
    /// </summary>
    private static readonly TimeSpan RedeliveryAfter = TimeSpan.FromMinutes(10);

    private int HeartbeatSeconds => Math.Clamp(options.Value.HeartbeatSeconds, 10, 3600);

    /// <summary>Registers an agent. Returns null if the token is invalid, expired or used up.</summary>
    public async Task<RegisterResponse?> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var hash = Secrets.Hash(request.Token);

        // "Consume" the token atomically: with concurrent requests exactly one wins.
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

        // A reinstallation replaces earlier credentials of the same device.
        await db.Credentials
            .Where(c => c.DeviceId == deviceId && c.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.RevokedAt, now), ct);

        var (credential, secret) = AgentCredential.Issue(deviceId);

        // Issue the certificate right at registration. An invalid CSR must not make the registration fail
        // (the token is already used up); the agent then requests the certificate again with the heartbeat.
        IssuedCertificate? issued = null;
        if (authority.Enabled && !string.IsNullOrWhiteSpace(request.Csr))
        {
            try
            {
                issued = authority.Issue(request.Csr, credential.Id, now);
                credential.SetCertificate(issued.Thumbprint, issued.NotAfter);
            }
            catch (CryptographicException ex)
            {
                logger.LogWarning(ex, "The certificate request sent during registration is invalid.");
            }
        }

        db.Credentials.Add(credential);
        await db.SaveChangesAsync(ct);

        return new RegisterResponse(
            credential.Id, secret, deviceId, HeartbeatSeconds,
            issued?.CertificatePem, issued is null ? null : authority.CaCertificatePem);
    }

    /// <summary>Issues a certificate (again) for an existing agent and stores the new thumbprint.</summary>
    public async Task<CertificateResponse?> IssueCertificateAsync(Guid agentId, string csrPem, CancellationToken ct)
    {
        if (!authority.Enabled)
        {
            return null;
        }

        var credential = await db.Credentials.SingleOrDefaultAsync(c => c.Id == agentId && c.RevokedAt == null, ct);
        if (credential is null)
        {
            return null;
        }

        var issued = authority.Issue(csrPem, credential.Id, DateTimeOffset.UtcNow);
        credential.SetCertificate(issued.Thumbprint, issued.NotAfter);
        await db.SaveChangesAsync(ct);
        return new CertificateResponse(issued.CertificatePem, authority.CaCertificatePem);
    }

    public async Task<HeartbeatResponse> HeartbeatAsync(
        Guid agentId, Guid deviceId, HeartbeatRequest request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // 1. Take over results of earlier jobs (only for jobs of this device)
        var completed = new List<AgentJob>();
        foreach (var result in (request.Results ?? []).Take(MaxResultsPerHeartbeat))
        {
            var job = await db.Jobs.SingleOrDefaultAsync(j => j.Id == result.JobId && j.DeviceId == deviceId, ct);
            if (job is not null && job.Complete(result.Success, result.Output, now))
            {
                completed.Add(job);
            }
        }

        // 2. Running jobs: extend the "lease" so that they are not delivered again
        var running = (request.RunningJobs ?? []).Take(MaxJobsPerHeartbeat * 5).ToList();
        if (running.Count > 0)
        {
            await db.Jobs
                .Where(j => running.Contains(j.Id) && j.DeviceId == deviceId && j.Status == JobStatus.Delivered)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.DeliveredAt, now), ct);
        }

        // 3. Update device information
        await devices.ReportAsync(
            deviceId,
            new DeviceReport(request.Hostname ?? "", request.OsFamily, request.OsName, request.OsVersion, request.AgentVersion),
            ct);

        await db.Credentials
            .Where(c => c.Id == agentId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastSeenAt, now), ct);

        // 4. Deliver due jobs
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

        // 5. Notify other modules (only after the state has been saved)
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
                    logger.LogError(ex, "Processing the result of job {JobId} failed.", job.Id);
                }
            }
        }

        return new HeartbeatResponse(
            due.Select(j => new AgentJobDto(j.Id, j.Type, j.Payload)).ToList(),
            HeartbeatSeconds,
            AgentUpdatePlanner.Plan(options.Value.Update, packages, request.Platform, request.AgentVersion, deviceId));
    }
}
