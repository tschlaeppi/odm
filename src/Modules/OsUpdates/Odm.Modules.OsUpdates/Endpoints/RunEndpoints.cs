using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Odm.Modules.OsUpdates.Application;
using Odm.Modules.OsUpdates.Domain;
using Odm.Modules.OsUpdates.Infrastructure;
using Odm.SharedKernel;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.OsUpdates.Endpoints;

internal sealed class HistoryRow
{
    public string Title { get; init; } = "";
    public Guid? RunId { get; init; }
    public Guid DeviceId { get; init; }
    public RunStatus Status { get; init; }
    public DateTimeOffset At { get; init; }
    public bool ViaOdm { get; init; }
}

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

        // History of individual updates (Windows updates / Linux packages), newest first.
        group.MapGet("/history", async (
            Guid? deviceId, Guid? runId, string? q, int? skip, int? take,
            OsUpdatesDbContext db, IDeviceDirectory directory, CancellationToken ct) =>
        {
            // ODM source: individual updates of the runs.
            var viaOdm = from i in db.RunItems.AsNoTracking()
                         join r in db.Runs.AsNoTracking() on i.RunId equals r.Id
                         select new HistoryRow
                         {
                             Title = i.Title, RunId = r.Id, DeviceId = r.DeviceId, Status = r.Status,
                             At = r.CompletedAt ?? r.CreatedAt, ViaOdm = true
                         };

            // OS source: history read from the operating system, without what falls into the time span of a finished ODM run
            // (those updates were installed by ODM, also for runs from before the per-run list existed).
            var external = db.ReportedUpdates.AsNoTracking()
                .Where(e => !db.Runs.Any(r =>
                    r.DeviceId == e.DeviceId && r.CompletedAt != null && e.InstalledAt >= r.CreatedAt && e.InstalledAt <= r.CompletedAt))
                .Select(e => new HistoryRow
                {
                    Title = e.Title, RunId = null, DeviceId = e.DeviceId, Status = RunStatus.Succeeded,
                    At = e.InstalledAt, ViaOdm = false
                });

            var query = runId is null ? viaOdm.Concat(external) : viaOdm;

            if (deviceId is { } device)
            {
                query = query.Where(x => x.DeviceId == device);
            }

            if (runId is { } run)
            {
                query = query.Where(x => x.RunId == run);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                query = query.Where(x => x.Title.ToLower().Contains(term));
            }

            var total = await query.CountAsync(ct);
            var rows = await query
                .OrderByDescending(x => x.At).ThenBy(x => x.Title)
                .Skip(Math.Max(skip ?? 0, 0)).Take(Math.Clamp(take ?? 100, 1, 500))
                .ToListAsync(ct);
            var devices = await directory.GetManyAsync(rows.Select(r => r.DeviceId).Distinct().ToList(), ct);

            return Results.Ok(new HistoryPageDto(total, rows.Select(r => new HistoryItemDto(
                r.RunId, r.DeviceId, devices.TryGetValue(r.DeviceId, out var d) ? d.Name : null,
                r.Title, r.Status.ToString(), r.At, r.ViaOdm)).ToList()));
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
