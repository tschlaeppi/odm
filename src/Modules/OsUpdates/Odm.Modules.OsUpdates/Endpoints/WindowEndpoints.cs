using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Odm.Modules.OsUpdates.Application;
using Odm.Modules.OsUpdates.Domain;
using Odm.Modules.OsUpdates.Infrastructure;
using Odm.SharedKernel;

namespace Odm.Modules.OsUpdates.Endpoints;

internal static class WindowEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/os-updates/windows").WithTags("OsUpdates").RequireAuthorization();

        group.MapGet("/", async (OsUpdatesDbContext db, CancellationToken ct) =>
            Results.Ok((await db.Windows.AsNoTracking().OrderBy(w => w.Name).ToListAsync(ct)).Select(ToDto)));

        group.MapPost("/", async (WindowRequest request, OsUpdatesDbContext db, CancellationToken ct) =>
            {
                if (!TryParse(request, out var name, out var days, out var errors))
                {
                    return Results.ValidationProblem(errors!);
                }

                var lowered = name.ToLower();
                if (await db.Windows.AnyAsync(w => w.Name.ToLower() == lowered, ct))
                {
                    return Results.Conflict();
                }

                var window = MaintenanceWindow.Create(name, days, request.StartMinutes, request.DurationMinutes, request.TimeZoneId!);
                db.Windows.Add(window);
                await db.SaveChangesAsync(ct);
                return Results.Created($"/api/os-updates/windows/{window.Id}", ToDto(window));
            })
            .RequireAuthorization(Policies.Operator);

        group.MapPut("/{id:guid}", async (Guid id, WindowRequest request, OsUpdatesDbContext db, CancellationToken ct) =>
            {
                if (!TryParse(request, out var name, out var days, out var errors))
                {
                    return Results.ValidationProblem(errors!);
                }

                var window = await db.Windows.SingleOrDefaultAsync(w => w.Id == id, ct);
                if (window is null)
                {
                    return Results.NotFound();
                }

                var lowered = name.ToLower();
                if (await db.Windows.AnyAsync(w => w.Id != id && w.Name.ToLower() == lowered, ct))
                {
                    return Results.Conflict();
                }

                window.Update(name, days, request.StartMinutes, request.DurationMinutes, request.TimeZoneId!);
                await db.SaveChangesAsync(ct);
                return Results.Ok(ToDto(window));
            })
            .RequireAuthorization(Policies.Operator);

        group.MapDelete("/{id:guid}", async (Guid id, OsUpdatesDbContext db, CancellationToken ct) =>
            {
                await db.Assignments.Where(a => a.Scope == AssignmentScope.Window && a.ParentId == id).ExecuteDeleteAsync(ct);
                var deleted = await db.Windows.Where(w => w.Id == id).ExecuteDeleteAsync(ct);
                return deleted == 0 ? Results.NotFound() : Results.NoContent();
            })
            .RequireAuthorization(Policies.Operator);
    }

    private static WindowDto ToDto(MaintenanceWindow w)
    {
        var days = Enum.GetValues<WeekDays>().Where(d => d != WeekDays.None && w.Days.HasFlag(d)).Select(d => d.ToString()).ToList();
        return new WindowDto(w.Id, w.Name, days, w.StartMinutes, w.DurationMinutes, w.TimeZoneId);
    }

    private static bool TryParse(WindowRequest request, out string name, out WeekDays days, out Dictionary<string, string[]>? errors)
    {
        errors = new Dictionary<string, string[]>();
        name = request.Name?.Trim() ?? "";
        days = WeekDays.None;

        if (name.Length is 0 or > 128) errors["name"] = ["Name is required (at most 128 characters)."];

        foreach (var day in request.Days ?? [])
        {
            if (Enum.TryParse<WeekDays>(day, true, out var parsed) && parsed != WeekDays.None && Enum.IsDefined(parsed))
            {
                days |= parsed;
            }
            else
            {
                errors["days"] = ["Invalid weekday."];
            }
        }

        if (days == WeekDays.None && !errors.ContainsKey("days")) errors["days"] = ["At least one weekday is required."];
        if (request.StartMinutes is < 0 or > 1439) errors["startMinutes"] = ["Start must be between 00:00 and 23:59."];
        if (request.DurationMinutes < MaintenanceWindow.MinDurationMinutes || request.DurationMinutes > MaintenanceWindow.MaxDurationMinutes)
            errors["durationMinutes"] = ["Duration must be between 15 minutes and 24 hours."];
        if (string.IsNullOrWhiteSpace(request.TimeZoneId) || request.TimeZoneId.Length > 64 || !MaintenanceWindow.IsValidTimeZone(request.TimeZoneId))
            errors["timeZoneId"] = ["Unknown time zone."];

        if (errors.Count == 0)
        {
            errors = null;
            return true;
        }

        return false;
    }
}
