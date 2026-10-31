using Microsoft.Extensions.Options;
using Odm.Agent.Update;

namespace Odm.Agent;

/// <summary>
/// Main loop: registers the agent (once) and reports to the server regularly.
/// Delivered jobs are handed to the <see cref="JobExecutor"/>. Errors lead to growing wait times.
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
                    // Wakes up early when a job has finished so that the result is reported immediately.
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
                logger.LogWarning(ex, "Connection to the server failed (attempt {Failures}). Next attempt in {Delay:N0} s.",
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
                "The agent is not registered and no Agent:RegistrationToken is configured.");
        }

        var info = systemInfo.Collect();

        // With https, create a key pair right away and send the certificate request along (mTLS, if the server requires it).
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

        logger.LogInformation("Agent registered (device {DeviceId}).", state.DeviceId);

        try
        {
            await store.ClearRegistrationTokenAsync(ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "The registration token could not be removed from the configuration file.");
        }

        return state;
    }

    /// <summary>Sends the heartbeat and returns the wait time until the next one.</summary>
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

        // Update only when the agent has nothing to do (no running jobs, no pending results).
        if (response.Update is { } offer && executor.ActiveJobIds().Count == 0 && executor.PendingResults().Count == 0)
        {
            await updater.TryUpdateAsync(offer, ct);
        }

        return TimeSpan.FromSeconds(Math.Max(10, response.NextHeartbeatSeconds));
    }

    private DateTimeOffset _lastCertificateAttempt = DateTimeOffset.MinValue;

    /// <summary>Fetches or renews the client certificate (new key); at most every 10 minutes, errors do not stop the agent.</summary>
    private async Task RequestCertificateAsync(AgentState state, CancellationToken ct)
    {
        if (DateTimeOffset.UtcNow - _lastCertificateAttempt < TimeSpan.FromMinutes(10))
        {
            return;
        }

        if (!options.Value.ServerUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return; // a client certificate cannot be presented over http
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
            logger.LogWarning(ex, "The client certificate could not be requested.");
        }
    }

    // 5 s, 10 s, 20 s ... at most 5 minutes, plus up to 20 % random jitter against simultaneous retries.
    private static TimeSpan Backoff(int failures)
    {
        var seconds = Math.Min(300, 5 * Math.Pow(2, Math.Min(failures - 1, 6)));
        return TimeSpan.FromSeconds(seconds * (1 + Random.Shared.NextDouble() * 0.2));
    }
}
