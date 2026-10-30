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

    /// <summary>Selbstaktualisierung der Agents (standardmäßig aus).</summary>
    public AgentUpdateOptions Update { get; set; } = new();

    /// <summary>Authentisierung der Agents per Client-Zertifikat.</summary>
    public AgentMtlsOptions Mtls { get; set; } = new();
}

public enum AgentMtlsMode
{
    /// <summary>Keine Zertifikate (nur Agent-Secret).</summary>
    Off,

    /// <summary>Zertifikate werden ausgestellt und geprüft; Agents ohne Zertifikat funktionieren weiter, bis sie eines haben.</summary>
    Optional,

    /// <summary>Wie Optional, aber Agents ohne Zertifikat erhalten nur ein Zertifikat, keine Aufträge oder Pakete.</summary>
    Required
}

public sealed class AgentMtlsOptions
{
    public AgentMtlsMode Mode { get; set; } = AgentMtlsMode.Off;

    /// <summary>Verzeichnis der Agent-CA (ca.crt, ca.key); wird beim ersten Start angelegt. Der Schlüssel liegt dort unverschlüsselt (0600).</summary>
    public string CaDirectory { get; set; } = "pki";

    /// <summary>Gültigkeit ausgestellter Agent-Zertifikate.</summary>
    public int CertificateValidityDays { get; set; } = 365;

    /// <summary>So viele Tage vor Ablauf fordert der Server den Agent zur Erneuerung auf.</summary>
    public int RenewBeforeDays { get; set; } = 30;
}

public enum AgentUpdateMode
{
    /// <summary>Der Server bietet keine Updates an.</summary>
    Off,

    /// <summary>Agents erhalten die höchste vorhandene Version (nur Aufwärts).</summary>
    Latest,

    /// <summary>Agents erhalten genau <see cref="AgentUpdateOptions.TargetVersion"/> (auch Rückstufung möglich).</summary>
    Pinned
}

public sealed class AgentUpdateOptions
{
    public AgentUpdateMode Mode { get; set; } = AgentUpdateMode.Off;

    /// <summary>Soll-Version bei <see cref="AgentUpdateMode.Pinned"/>, z. B. 0.4.1.</summary>
    public string TargetVersion { get; set; } = "";

    /// <summary>Anteil der Geräte (0–100), die das Update angeboten bekommen. Stabil je Gerät; schrittweise erhöhen für gestaffelte Verteilung.</summary>
    public int RolloutPercent { get; set; } = 100;
}

// ---- Agent-Protokoll (/api/agent/v1) ----

public sealed record RegisterRequest(
    string Token, string Hostname, string OsFamily, string OsName, string OsVersion, string AgentVersion, string? Csr = null);

public sealed record RegisterResponse(
    Guid AgentId, string Secret, Guid DeviceId, int HeartbeatSeconds,
    string? Certificate = null, string? CaCertificate = null);

public sealed record CertificateRequestDto(string Csr);

public sealed record CertificateResponse(string Certificate, string CaCertificate);

public sealed record JobResultDto(Guid JobId, bool Success, string? Output);

public sealed record HeartbeatRequest(
    string OsFamily, string OsName, string OsVersion, string AgentVersion,
    List<JobResultDto>? Results, List<Guid>? RunningJobs, string? Platform = null, string? Hostname = null);

public sealed record AgentJobDto(Guid Id, string Type, string? Payload);

/// <summary>Angebot für ein Agent-Update; <c>Url</c> ist relativ zur Server-Adresse.</summary>
public sealed record AgentUpdateOfferDto(string Version, string Sha256, long Size, string Url);

public sealed record HeartbeatResponse(
    IReadOnlyList<AgentJobDto> Jobs, int NextHeartbeatSeconds, AgentUpdateOfferDto? Update = null,
    bool CertificateRequested = false);

// ---- Verwaltung (Web-UI) ----

public sealed record CreateTokenRequest(string? Label, int? ValidHours);

public sealed record TokenDto(Guid Id, string Label, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? UsedAt, string Status);

/// <summary>Einmalige Antwort beim Erstellen: enthält als einzige den Klartext des Tokens.</summary>
public sealed record TokenCreatedDto(Guid Id, string Label, string Token, DateTimeOffset ExpiresAt);

public sealed record CreateJobRequest(Guid DeviceId, string? Type, string? Payload);

public sealed record JobViewDto(
    Guid Id, Guid DeviceId, string Type, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, string? Output);
