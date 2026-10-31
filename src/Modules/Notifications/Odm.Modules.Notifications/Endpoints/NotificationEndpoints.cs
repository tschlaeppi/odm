using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Odm.Modules.Notifications.Application;
using Odm.Modules.Notifications.Infrastructure;
using Odm.SharedKernel;

namespace Odm.Modules.Notifications.Endpoints;

internal static class NotificationEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/notifications").WithTags("Notifications").RequireAuthorization(Policies.Admin);

        // Settings summary (no secrets) and the most recent alerts.
        group.MapGet("/", async (NotificationsDbContext db, IOptions<NotificationOptions> options, NotificationService service, CancellationToken ct) =>
        {
            var settings = options.Value;
            var recent = await db.Sent.AsNoTracking()
                .OrderByDescending(s => s.SentAt)
                .Take(100)
                .Select(s => new SentAlertDto(s.Id, s.Type, s.DeviceName, s.Subject, s.Detail, s.SentAt, s.Channels))
                .ToListAsync(ct);

            return Results.Ok(new
            {
                settings.Enabled,
                Channels = service.ChannelInfos(),
                Events = settings.Events.Length == 0 ? Odm.SharedKernel.Contracts.AlertTypes.All : settings.Events,
                Recent = recent
            });
        });

        group.MapPost("/test", async (NotificationService service, CancellationToken ct) =>
        {
            var results = await service.SendTestAsync(ct);
            return results.Count == 0
                ? Results.Problem("No notification channel is configured.", statusCode: 400)
                : Results.Ok(results);
        });
    }
}
