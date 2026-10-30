using Microsoft.EntityFrameworkCore;
using Odm.Modules.Agents.Domain;
using Odm.Modules.Agents.Infrastructure;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.Agents.Application;

internal sealed class JobQueue(AgentsDbContext db) : IJobQueue
{
    public async Task<Guid?> EnqueueAsync(Guid deviceId, string type, string? payload, CancellationToken ct)
    {
        if (!JobTypes.All.Contains(type))
        {
            throw new ArgumentException($"Unbekannter Auftragstyp '{type}'.", nameof(type));
        }

        var hasAgent = await db.Credentials.AnyAsync(c => c.DeviceId == deviceId && c.RevokedAt == null, ct);
        if (!hasAgent)
        {
            return null;
        }

        var job = AgentJob.Create(deviceId, type, payload);
        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);
        return job.Id;
    }
}
