using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Odm.Modules.Notifications.Application;

namespace Odm.Modules.Notifications.Infrastructure;

/// <summary>Checks the alert sources every <c>Notifications:CheckMinutes</c> minutes and cleans up old records once a day.</summary>
public sealed class NotificationWorker(
    IServiceScopeFactory scopes, IOptions<NotificationOptions> options, ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        // Let the start-up (migrations) finish first.
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        var interval = TimeSpan.FromMinutes(Math.Clamp(options.Value.CheckMinutes, 1, 1440));
        using var timer = new PeriodicTimer(interval);
        var lastCleanup = DateTimeOffset.MinValue;
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var sent = await scope.ServiceProvider.GetRequiredService<NotificationService>().RunAsync(DateTimeOffset.UtcNow, stoppingToken);
                if (sent > 0)
                {
                    logger.LogInformation("Sent {Count} alerts.", sent);
                }

                if (DateTimeOffset.UtcNow - lastCleanup > TimeSpan.FromHours(24) && options.Value.RetentionDays > 0)
                {
                    var cutoff = DateTimeOffset.UtcNow.AddDays(-options.Value.RetentionDays);
                    await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>()
                        .Sent.Where(s => s.SentAt < cutoff).ExecuteDeleteAsync(stoppingToken);
                    lastCleanup = DateTimeOffset.UtcNow;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Notification run failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
