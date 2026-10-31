using System.Text.Json;
using Odm.Agent.Repo;

namespace Odm.Agent.Jobs;

/// <summary>Installiert bzw. aktualisiert Pakete aus Repositories (Winget, Chocolatey, APT, RPM, Flatpak, Snap).</summary>
public sealed class InstallRepoJobHandler(ILogger<InstallRepoJobHandler> logger) : IJobHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] WindowsManagers = ["Winget", "Chocolatey"];

    public string Type => "install-repo";

    public async Task<JobOutcome> HandleAsync(ServerJob job, JobContext context, CancellationToken ct)
    {
        InstallRepoPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<InstallRepoPayload>(job.Payload ?? "", Json);
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Identifier))
        {
            return new JobOutcome(false, "Invalid job content.");
        }

        var needsWindows = WindowsManagers.Contains(payload.Manager);
        if (needsWindows != OperatingSystem.IsWindows())
        {
            return new JobOutcome(false, $"{payload.Manager} is not supported on this operating system.");
        }

        ResolvedArguments arguments;
        try
        {
            // Zusatzargumente gelten für die Installation, nicht für die Deinstallation.
            arguments = await ResolvedArguments.ResolveAsync(payload.Uninstall ? null : payload.Arguments, job, context, ct);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            return new JobOutcome(false, ex.Message);
        }

        // Befehl und Argumente werden bewusst nicht protokolliert: sie können Secrets enthalten.
        logger.LogInformation("{Action} {Identifier} with {Manager}.", payload.Uninstall ? "Uninstalling" : "Installing", payload.Identifier, payload.Manager);
        var timeout = TimeSpan.FromMinutes(Math.Clamp(payload.TimeoutMinutes, 1, 24 * 60));
        var result = await RepoInstaller.RunAsync(payload, arguments.Tokens, timeout, ct);

        return new JobOutcome(result.Success, SecretSubstitution.Redact(result.Output, arguments.SecretValues));
    }
}
