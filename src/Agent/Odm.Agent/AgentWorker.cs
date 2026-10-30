using Microsoft.Extensions.Options;
using Odm.Agent.Update;

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
    AgentUpdater updater,
    ClientCertificateStore certificates,
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

        // Bei https gleich ein Schlüsselpaar erzeugen und die Zertifikatsanforderung mitschicken (mTLS, falls der Server es verlangt).
        System.Security.Cryptography.ECDsa? key = null;
        string? csr = null;
        if (options.Value.ServerUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            (key, csr) = certificates.CreateRequest();
        }

        RegisterResponse response;
        try
        {
            response = await server.RegisterAsync(
                new RegisterRequest(token, info.Hostname, info.OsFamily, info.OsName, info.OsVersion, SystemInfoProvider.AgentVersion, csr),
                ct);
        }
        catch
        {
            key?.Dispose();
            throw;
        }

        var state = new AgentState(response.AgentId, response.Secret, response.DeviceId);
        await store.SaveAsync(state, ct);
        if (key is not null)
        {
            try
            {
                if (response.Certificate is not null)
                {
                    certificates.Save(key, response.Certificate);
                }
            }
            finally
            {
                key.Dispose();
            }
        }

        logger.LogInformation("Agent registriert (Gerät {DeviceId}).", state.DeviceId);

        try
        {
            await store.ClearRegistrationTokenAsync(ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Das Registrierungs-Token konnte nicht aus der Konfigurationsdatei entfernt werden.");
        }

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
                results, executor.ActiveJobIds(), AgentUpdater.Platform, info.Hostname),
            ct);

        executor.Acknowledge(results);

        foreach (var job in response.Jobs)
        {
            executor.Enqueue(job, state);
        }

        updater.ConfirmStartup();

        if (response.CertificateRequested)
        {
            await RequestCertificateAsync(state, ct);
        }

        // Update nur, wenn der Agent nichts zu tun hat (keine laufenden Aufträge, keine offenen Ergebnisse).
        if (response.Update is { } offer && executor.ActiveJobIds().Count == 0 && executor.PendingResults().Count == 0)
        {
            await updater.TryUpdateAsync(offer, ct);
        }

        return TimeSpan.FromSeconds(Math.Max(10, response.NextHeartbeatSeconds));
    }

    private DateTimeOffset _lastCertificateAttempt = DateTimeOffset.MinValue;

    /// <summary>Holt bzw. erneuert das Client-Zertifikat (neuer Schlüssel); höchstens alle 10 Minuten, Fehler stoppen den Agent nicht.</summary>
    private async Task RequestCertificateAsync(AgentState state, CancellationToken ct)
    {
        if (DateTimeOffset.UtcNow - _lastCertificateAttempt < TimeSpan.FromMinutes(10))
        {
            return;
        }

        _lastCertificateAttempt = DateTimeOffset.UtcNow;
        try
        {
            var (key, csr) = certificates.CreateRequest();
            using (key)
            {
                var response = await server.RequestCertificateAsync(state, csr, ct);
                certificates.Save(key, response.Certificate);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Das Client-Zertifikat konnte nicht angefordert werden.");
        }
    }

    // 5 s, 10 s, 20 s ... maximal 5 Minuten, plus bis zu 20 % Zufall gegen gleichzeitige Wiederholungen.
    private static TimeSpan Backoff(int failures)
    {
        var seconds = Math.Min(300, 5 * Math.Pow(2, Math.Min(failures - 1, 6)));
        return TimeSpan.FromSeconds(seconds * (1 + Random.Shared.NextDouble() * 0.2));
    }
}
