namespace Odm.Agent.Os;

/// <summary>Inhalt des Auftrags "os-update" (Gegenstück zum Server).</summary>
public sealed record OsUpdatePayload(string Mode, bool AllowReboot, DateTimeOffset WindowEndsAt, int TimeoutMinutes);

public sealed record OsUpdateResult(
    bool Success, int Installed, int Failed, IReadOnlyList<string> Titles, bool RebootRequired, string? Message);

/// <summary>Installiert Betriebssystem-Updates; je Betriebssystem eine Implementierung.</summary>
public interface IOsUpdater
{
    Task<OsUpdateResult> RunAsync(OsUpdatePayload payload, string workDirectory, CancellationToken ct);
}
