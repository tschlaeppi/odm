using System.Text.Json;
using Odm.Agent.Os;

namespace Odm.Agent.Jobs;

/// <summary>Ausgabe des Auftrags (JSON), vom Server ausgewertet.</summary>
public sealed record OsUpdateOutput(
    int Installed, int Failed, IReadOnlyList<string> Titles, bool RebootRequired, bool Rebooting, string? Message);

/// <summary>
/// Runs an update run in the maintenance window and schedules the restart if needed.
/// The restart is delayed so that the agent can report the result first.
/// </summary>
public sealed class OsUpdateJobHandler(
    WindowsUpdater windows,
    LinuxUpdater linux,
    ILogger<OsUpdateJobHandler> logger) : IJobHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A job that is started later than (window end - buffer) is no longer executed.</summary>
    private static readonly TimeSpan StartBuffer = TimeSpan.FromMinutes(10);

    /// <summary>A restart is only scheduled if that much time remains until the end of the window.</summary>
    private static readonly TimeSpan RebootBuffer = TimeSpan.FromMinutes(5);

    private const int RebootDelaySeconds = 120;

    public string Type => "os-update";

    public async Task<JobOutcome> HandleAsync(ServerJob job, JobContext context, CancellationToken ct)
    {
        OsUpdatePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<OsUpdatePayload>(job.Payload ?? "", Json);
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null)
        {
            return Failure("Invalid job content.");
        }

        if (DateTimeOffset.UtcNow >= payload.WindowEndsAt - StartBuffer)
        {
            return Failure("The maintenance window had (almost) ended at start; the run was not started.");
        }

        var updater = OperatingSystem.IsWindows() ? (IOsUpdater)windows : linux;
        var workDirectory = context.Store.CreateWorkDirectory(job.Id);
        try
        {
            logger.LogInformation("Starting OS updates (mode {Mode}).", payload.Mode);
            var result = await updater.RunAsync(payload, workDirectory, ct);

            var rebooting = false;
            if (result.RebootRequired && payload.AllowReboot && DateTimeOffset.UtcNow < payload.WindowEndsAt - RebootBuffer)
            {
                rebooting = await TryScheduleRebootAsync(ct);
            }

            var output = new OsUpdateOutput(result.Installed, result.Failed, result.Titles, result.RebootRequired, rebooting, result.Message);
            return new JobOutcome(result.Success, JsonSerializer.Serialize(output, Json));
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch (Exception)
            {
                // Cleaning up is best effort
            }
        }
    }

    private async Task<bool> TryScheduleRebootAsync(CancellationToken ct)
    {
        const string reason = "Open Device Manager: Neustart nach Updates";
        try
        {
            ProcessRunner.Result result;
            if (OperatingSystem.IsWindows())
            {
                result = await ProcessRunner.RunAsync(
                    Path.Combine(Environment.SystemDirectory, "shutdown.exe"),
                    ["/r", "/t", RebootDelaySeconds.ToString(), "/c", reason],
                    TimeSpan.FromMinutes(1), ct);
            }
            else
            {
                var shutdown = new[] { "/usr/sbin/shutdown", "/sbin/shutdown", "/usr/bin/shutdown" }.FirstOrDefault(File.Exists) ?? "shutdown";
                result = await ProcessRunner.RunAsync(
                    shutdown, ["-r", $"+{RebootDelaySeconds / 60}", reason], TimeSpan.FromMinutes(1), ct);
            }

            logger.LogInformation("Restart scheduled in {Seconds} s (exit code {Code}).", RebootDelaySeconds, result.ExitCode);
            return result.ExitCode == 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Restart could not be scheduled.");
            return false;
        }
    }

    private static JobOutcome Failure(string message) =>
        new(false, JsonSerializer.Serialize(new OsUpdateOutput(0, 0, [], false, false, message), Json));
}
