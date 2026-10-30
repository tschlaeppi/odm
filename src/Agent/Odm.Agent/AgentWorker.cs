using Microsoft.Extensions.Options;

namespace Odm.Agent;

/// <summary>
/// Hauptschleife: registriert den Agent (einmalig) und meldet sich regelmässig beim Server.
/// Ausgelieferte Aufträge übergibt er dem <see cref="JobExecutor"/>. Fehler führen zu wachsender Wartezeit.
/// </summary>
public sealed class AgentWorker(
    IOptions<AgentOptions> options,
    AgentStateStore store,
    ServerClient server,
    SystemInfoProvider systemInfo,
    JobExecutor executor,
    ILogger<AgentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        var delay = TimeSpan.Zero;
        AgentState? state = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (delay > TimeSpan.Zero)
                {
                    // Wacht vorzeitig auf, wenn ein Auftrag fertig ist, damit das Ergebnis sofort gemeldet wird.
                    await executor.WaitAsync(delay, stoppingToken);
                }

                state ??= await store.LoadAsync(stoppingToken) ?? await RegisterAsync(stoppingToken);
                delay = await HeartbeatAsync(state, stoppingToken);
                failures = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                failures++;
                delay = Backoff(failures);
                logger.LogWarning(ex, "Verbindung zum Server fehlgeschlagen (Versuch {Failures}). Nächster Versuch in {Delay:N0} s.",
                    failures, delay.TotalSeconds);
            }
        }
    }

    private async Task<AgentState> RegisterAsync(CancellationToken ct)
    {
        var token = options.Value.RegistrationToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "Der Agent ist nicht registriert und es ist kein Agent:RegistrationToken konfiguriert.");
        }

        var info = systemInfo.Collect();
        var response = await server.RegisterAsync(
            new RegisterRequest(token, info.Hostname, info.OsFamily, info.OsName, info.OsVersion, SystemInfoProvider.AgentVersion),
            ct);

        var state = new AgentState(response.AgentId, response.Secret, response.DeviceId);
        await store.SaveAsync(state, ct);
        logger.LogInformation("Agent registriert (Gerät {DeviceId}).", state.DeviceId);
        return state;
    }

    /// <summary>Sendet den Heartbeat und liefert die Wartezeit bis zum nächsten.</summary>
    private async Task<TimeSpan> HeartbeatAsync(AgentState state, CancellationToken ct)
    {
        var info = systemInfo.Collect();
        var results = executor.PendingResults();

        var response = await server.HeartbeatAsync(
            state,
            new HeartbeatRequest(
                info.OsFamily, info.OsName, info.OsVersion, SystemInfoProvider.AgentVersion,
                results, executor.ActiveJobIds()),
            ct);

        executor.Acknowledge(results);

        foreach (var job in response.Jobs)
        {
            executor.Enqueue(job, state);
        }

        return TimeSpan.FromSeconds(Math.Max(10, response.NextHeartbeatSeconds));
    }

    // 5 s, 10 s, 20 s ... maximal 5 Minuten, plus bis zu 20 % Zufall gegen gleichzeitige Wiederholungen.
    private static TimeSpan Backoff(int failures)
    {
        var seconds = Math.Min(300, 5 * Math.Pow(2, Math.Min(failures - 1, 6)));
        return TimeSpan.FromSeconds(seconds * (1 + Random.Shared.NextDouble() * 0.2));
    }
}
