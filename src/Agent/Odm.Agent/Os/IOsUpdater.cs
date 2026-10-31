namespace Odm.Agent.Os;

/// <summary>Content of the job "os-update" (counterpart of the server).</summary>
public sealed record OsUpdatePayload(string Mode, bool AllowReboot, DateTimeOffset WindowEndsAt, int TimeoutMinutes);

public sealed record OsUpdateResult(
    bool Success, int Installed, int Failed, IReadOnlyList<string> Titles, bool RebootRequired, string? Message);

/// <summary>Installs operating system updates; one implementation per operating system.</summary>
public interface IOsUpdater
{
    Task<OsUpdateResult> RunAsync(OsUpdatePayload payload, string workDirectory, CancellationToken ct);
}
