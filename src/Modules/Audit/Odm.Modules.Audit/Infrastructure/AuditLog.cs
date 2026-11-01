using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Odm.Modules.Audit.Domain;
using Odm.SharedKernel.Contracts;
using Odm.SharedKernel.Text;

namespace Odm.Modules.Audit.Infrastructure;

public sealed class AuditLog(AuditDbContext db, IHttpContextAccessor accessor, TimeProvider clock, ILogger<AuditLog> logger) : IAuditLog
{
    public async Task RecordAsync(AuditEvent e, CancellationToken ct = default)
    {
        try
        {
            var http = accessor.HttpContext;
            var actor = !string.IsNullOrWhiteSpace(e.Actor) ? e.Actor : ActorOf(http?.User);
            var role = e.Role ?? http?.User.FindFirstValue("role");

            db.Entries.Add(AuditEntry.Create(
                clock.GetUtcNow(), actor, role, e.Action, e.Target, e.Outcome,
                http?.Response.StatusCode is { } status && status != 200 ? status : null,
                http?.Connection.RemoteIpAddress?.ToString(), e.Details));
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The audit log must never break the action it records.
            logger.LogError(ex, "Could not write audit entry {Action}.", LogSanitizer.Clean(e.Action));
        }
    }

    /// <summary>User name, <c>agent:{id}</c> for agents, otherwise <c>anonymous</c>.</summary>
    public static string ActorOf(ClaimsPrincipal? user)
    {
        if (user?.FindFirstValue("name") is { Length: > 0 } name)
        {
            return name;
        }

        return user?.FindFirstValue("agent_id") is { Length: > 0 } agent ? $"agent:{agent}" : "anonymous";
    }
}
