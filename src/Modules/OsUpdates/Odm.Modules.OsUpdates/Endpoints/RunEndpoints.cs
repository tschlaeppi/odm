using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Odm.Modules.OsUpdates.Application;
using Odm.Modules.OsUpdates.Infrastructure;
using Odm.SharedKernel;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.OsUpdates.Endpoints;

internal static class RunEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/os-updates").WithTags("OsUpdates").RequireAuthorization();

        group.MapGet("/runs", async (Guid? deviceId, int? take, OsUpdatesDbContext db, IDeviceDirectory directory, CancellationToken ct) =>
        {
            var query = db.Runs.AsNoTracking().AsQueryable();
            if (deviceId is { } id)
            {
                query = query.Where(r => r.DeviceId == id);
            }

            var rows = await query.OrderByDescending(r => r.CreatedAt).Take(Math.Clamp(take ?? 100, 1, 500)).ToListAsync(ct);
            var windows = await db.Windows.AsNoTracking().ToDictionaryAsync(w => w.Id, w => w.Name, ct);
            var devices = await directory.GetManyAsync(rows.Select(r => r.DeviceId).ToList(), ct);

            return Results.Ok(rows.Select(r => new RunDto(
                r.Id, r.DeviceId,
                devices.TryGetValue(r.DeviceId, out var device) ? device.Name : null,
                windows.GetValueOrDefault(r.WindowId),
                r.OccurrenceStart, r.Status.ToString(), r.Installed, r.Failed, r.RebootRequired, r.Rebooting,
                r.Message, r.CreatedAt, r.CompletedAt)));
        });

        group.MapPost("/devices/{deviceId:guid}/run", async (Guid deviceId, UpdateSchedulingService scheduling, CancellationToken ct) =>
            {
                return await scheduling.RunNowAsync(deviceId, ct) switch
                {
                    RunNowResult.Started => Results.NoContent(),
                    RunNowResult.NoPolicy => Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["policy"] = ["No update policy applies to this device."]
                    }),
                    _ => Results.Conflict()
                };
            })
            .RequireAuthorization(Policies.Operator);
    }
}
