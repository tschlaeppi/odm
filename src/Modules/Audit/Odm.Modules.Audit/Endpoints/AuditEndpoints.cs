using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Odm.Modules.Audit.Application;
using Odm.Modules.Audit.Infrastructure;
using Odm.SharedKernel;

namespace Odm.Modules.Audit.Endpoints;

internal static class AuditEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Read-only, administrators only. There is deliberately no endpoint to change or delete entries.
        endpoints.MapGroup("/audit").WithTags("Audit")
            .MapGet("/", async (
                    AuditDbContext db, DateTimeOffset? from, DateTimeOffset? to, string? actor, string? outcome, string? text,
                    int? skip, int? take, CancellationToken ct) =>
                {
                    IQueryable<Domain.AuditEntry> query = db.Entries.AsNoTracking();

                    if (from is { } f) query = query.Where(e => e.Timestamp >= f);
                    if (to is { } t) query = query.Where(e => e.Timestamp <= t);
                    if (!string.IsNullOrWhiteSpace(actor))
                    {
                        var a = actor.Trim().ToLower();
                        query = query.Where(e => e.Actor.ToLower().Contains(a));
                    }

                    if (!string.IsNullOrWhiteSpace(outcome))
                    {
                        var o = outcome.Trim();
                        query = query.Where(e => e.Outcome == o);
                    }

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var x = text.Trim().ToLower();
                        query = query.Where(e => e.Action.ToLower().Contains(x) ||
                                                 (e.Target != null && e.Target.ToLower().Contains(x)) ||
                                                 (e.Details != null && e.Details.ToLower().Contains(x)));
                    }

                    var total = await query.CountAsync(ct);
                    var items = await query
                        .OrderByDescending(e => e.Timestamp)
                        .Skip(Math.Max(0, skip ?? 0))
                        .Take(Math.Clamp(take ?? 100, 1, 500))
                        .Select(e => new AuditEntryDto(
                            e.Id, e.Timestamp, e.Actor, e.Role, e.Action, e.Target, e.Outcome, e.StatusCode, e.IpAddress, e.Details))
                        .ToListAsync(ct);

                    return Results.Ok(new AuditPage(total, items));
                })
            .RequireAuthorization(Policies.Admin);
    }
}
