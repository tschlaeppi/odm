using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Odm.Modules.Audit.Application;

namespace Odm.Modules.Audit.Infrastructure;

/// <summary>Deletes audit entries older than <c>Audit:RetentionDays</c> (once a day; 0 = keep forever).</summary>
public sealed class AuditRetentionService(
    IServiceScopeFactory scopes, IOptions<AuditOptions> options, TimeProvider clock, ILogger<AuditRetentionService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the start-up (migrations) finish first.
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                await CleanAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Audit retention cleanup failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task<int> CleanAsync(CancellationToken ct)
    {
        var days = options.Value.RetentionDays;
        if (days <= 0)
        {
            return 0;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var cutoff = clock.GetUtcNow().AddDays(-days);
        var deleted = await db.Entries.Where(e => e.Timestamp < cutoff).ExecuteDeleteAsync(ct);
        if (deleted > 0)
        {
            logger.LogInformation("Deleted {Count} audit entries older than {Days} days.", deleted, days);
        }

        return deleted;
    }
}
