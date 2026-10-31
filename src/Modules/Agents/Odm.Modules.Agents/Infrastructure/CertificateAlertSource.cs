using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.Agents.Infrastructure;

/// <summary>
/// Agent certificates that expire within <c>Notifications:CertificateDays</c> days (default 7). Agents renew on their own
/// 30 days before expiry, so an alert means the renewal does not work (agent offline, certificates not reachable ...).
/// </summary>
internal sealed class CertificateAlertSource(AgentsDbContext db, IConfiguration configuration) : IAlertSource
{
    public async Task<IReadOnlyList<AlertCandidate>> GetAsync(DateTimeOffset now, CancellationToken ct)
    {
        var days = Math.Max(1, configuration.GetValue("Notifications:CertificateDays", 7));
        var limit = now.AddDays(days);

        var expiring = await db.Credentials.AsNoTracking()
            .Where(c => c.RevokedAt == null && c.CertNotAfter != null && c.CertNotAfter < limit)
            .Select(c => new { c.Id, c.DeviceId, c.CertNotAfter })
            .ToListAsync(ct);

        return expiring
            .Select(c => new AlertCandidate(
                AlertTypes.CertificateExpiring,
                $"cert:{c.Id:N}:{c.CertNotAfter!.Value.UtcTicks}",
                c.DeviceId,
                "Agent certificate",
                c.CertNotAfter < now
                    ? $"The agent certificate expired on {c.CertNotAfter:yyyy-MM-dd} UTC."
                    : $"The agent certificate expires on {c.CertNotAfter:yyyy-MM-dd} UTC."))
            .ToList();
    }
}
