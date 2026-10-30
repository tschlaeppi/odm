namespace Odm.Modules.Agents.Application;

public sealed class AgentsOptions
{
    public const string SectionName = "Agents";

    /// <summary>Intervall, in dem Agents sich melden.</summary>
    public int HeartbeatSeconds { get; set; } = 60;

    /// <summary>Verzeichnis mit den vorgebauten Agent-Paketen (relativ zum Content-Root oder absolut).</summary>
    public string PackagesPath { get; set; } = "agent-packages";

    /// <summary>
    /// Öffentliche Basisadresse des Servers für die Installationsskripte (z. B. https://odm.example.com).
    /// Leer = aus der Anfrage abgeleitet. Hinter einem Proxy sollte sie gesetzt werden.
    /// </summary>
    public string PublicBaseUrl { get; set; } = "";
}

// ---- Agent-Protokoll (/api/agent/v1) ----

public sealed record RegisterRequest(
    string Token, string Hostname, string OsFamily, string OsName, string OsVersion, string AgentVersion);

public sealed record RegisterResponse(Guid AgentId, string Secret, Guid DeviceId, int HeartbeatSeconds);

public sealed record JobResultDto(Guid JobId, bool Success, string? Output);

public sealed record HeartbeatRequest(
    string OsFamily, string OsName, string OsVersion, string AgentVersion,
    List<JobResultDto>? Results, List<Guid>? RunningJobs);

public sealed record AgentJobDto(Guid Id, string Type, string? Payload);

public sealed record HeartbeatResponse(IReadOnlyList<AgentJobDto> Jobs, int NextHeartbeatSeconds);

// ---- Verwaltung (Web-UI) ----

public sealed record CreateTokenRequest(string? Label, int? ValidHours);

public sealed record TokenDto(Guid Id, string Label, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? UsedAt, string Status);

/// <summary>Einmalige Antwort beim Erstellen: enthält als einzige den Klartext des Tokens.</summary>
public sealed record TokenCreatedDto(Guid Id, string Label, string Token, DateTimeOffset ExpiresAt);

public sealed record CreateJobRequest(Guid DeviceId, string? Type, string? Payload);

public sealed record JobViewDto(
    Guid Id, Guid DeviceId, string Type, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, string? Output);
