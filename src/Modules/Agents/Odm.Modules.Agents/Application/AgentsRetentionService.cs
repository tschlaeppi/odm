using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Odm.Modules.Agents.Domain;
using Odm.Modules.Agents.Infrastructure;

namespace Odm.Modules.Agents.Application;

/// <summary>
/// Housekeeping: deletes finished jobs (<c>Agents:JobRetentionDays</c>) and used or expired registration tokens
/// (<c>Agents:TokenRetentionDays</c>) once they are older than the configured number of days. 0 = keep forever.
/// Runs once a day. Jobs that are still pending or running are never touched.
/// </summary>
public sealed class AgentsRetentionService(
    IServiceScopeFactory scopes, IOptions<AgentsOptions> options, ILogger<AgentsRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the start-up (migrations) finish first.
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                await CleanAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Agent data cleanup failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task<(int Jobs, int Tokens)> CleanAsync(DateTimeOffset now, CancellationToken ct)
    {
        var settings = options.Value;
        if (settings.JobRetentionDays <= 0 && settings.TokenRetentionDays <= 0)
        {
            return (0, 0);
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentsDbContext>();

        var jobs = 0;
        if (settings.JobRetentionDays > 0)
        {
            var cutoff = now.AddDays(-settings.JobRetentionDays);
            jobs = await db.Jobs
                .Where(j => (j.Status == JobStatus.Succeeded || j.Status == JobStatus.Failed) && j.CompletedAt < cutoff)
                .ExecuteDeleteAsync(ct);
        }

        var tokens = 0;
        if (settings.TokenRetentionDays > 0)
        {
            var cutoff = now.AddDays(-settings.TokenRetentionDays);
            tokens = await db.Tokens
                .Where(t => t.ExpiresAt < cutoff || t.UsedAt < cutoff)
                .ExecuteDeleteAsync(ct);
        }

        if (jobs + tokens > 0)
        {
            logger.LogInformation("Housekeeping deleted {Jobs} finished jobs and {Tokens} old registration tokens.", jobs, tokens);
        }

        return (jobs, tokens);
    }
}
