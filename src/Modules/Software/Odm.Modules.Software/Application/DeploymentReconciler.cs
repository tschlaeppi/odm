using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Odm.Modules.Software.Application;

/// <summary>Hintergrunddienst, der den Soll-/Ist-Abgleich regelmässig und auf Anforderung ausführt.</summary>
public sealed class DeploymentReconciler(
    IServiceScopeFactory scopes,
    DeploymentTrigger trigger,
    IOptions<SoftwareOptions> options,
    ILogger<DeploymentReconciler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<DeploymentService>().ReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Software deployment reconciliation failed.");
            }

            try
            {
                var seconds = Math.Clamp(options.Value.ReconcileSeconds, 5, 3600);
                await trigger.WaitAsync(TimeSpan.FromSeconds(seconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
