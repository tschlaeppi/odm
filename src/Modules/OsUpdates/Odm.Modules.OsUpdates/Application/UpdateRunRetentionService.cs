using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Odm.Modules.OsUpdates.Infrastructure;

namespace Odm.Modules.OsUpdates.Application;

/// <summary>
/// Housekeeping: deletes finished update runs older than <c>OsUpdates:RunRetentionDays</c> (default 365, 0 = keep forever).
/// Runs once a day. Runs that are queued or running are never touched.
/// </summary>
public sealed class UpdateRunRetentionService(
    IServiceScopeFactory scopes, IConfiguration configuration, ILogger<UpdateRunRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the start-up (migrations) finish first.
        await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                await CleanAsync(configuration.GetValue("OsUpdates:RunRetentionDays", 365), DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Update run cleanup failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task<int> CleanAsync(int retentionDays, DateTimeOffset now, CancellationToken ct)
    {
        if (retentionDays <= 0)
        {
            return 0;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OsUpdatesDbContext>();
        var cutoff = now.AddDays(-retentionDays);
        var deleted = await db.Runs.Where(r => r.CompletedAt != null && r.CompletedAt < cutoff).ExecuteDeleteAsync(ct);
        if (deleted > 0)
        {
            logger.LogInformation("Housekeeping deleted {Count} update runs older than {Days} days.", deleted, retentionDays);
        }

        return deleted;
    }
}
