namespace Odm.Modules.Agents.Application;

public sealed class AgentsOptions
{
    public const string SectionName = "Agents";

    /// <summary>Interval at which agents report in.</summary>
    public int HeartbeatSeconds { get; set; } = 60;

    /// <summary>Directory with the prebuilt agent packages (relative to the content root or absolute).</summary>
    public string PackagesPath { get; set; } = "agent-packages";

    /// <summary>
    /// Public base address of the server for the install scripts (e.g. https://odm.example.com).
    /// Empty = derived from the request. Should be set behind a proxy.
    /// </summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>Finished jobs older than this many days are deleted daily. 0 = keep forever.</summary>
    public int JobRetentionDays { get; set; } = 90;

    /// <summary>Used or expired registration tokens older than this many days are deleted daily. 0 = keep forever.</summary>
    public int TokenRetentionDays { get; set; } = 30;

    /// <summary>Self-update of the agents (off by default).</summary>
    public AgentUpdateOptions Update { get; set; } = new();

    /// <summary>Agent authentication by client certificate.</summary>
    public AgentMtlsOptions Mtls { get; set; } = new();
}

public enum AgentMtlsMode
{
    /// <summary>No certificates (agent secret only).</summary>
    Off,

    /// <summary>Certificates are issued and verified; agents without a certificate keep working until they have one.</summary>
    Optional,

    /// <summary>Like Optional, but agents without a certificate only receive a certificate, no jobs or packages.</summary>
    Required
}

public sealed record InventoryHardwareDto(
    string? Manufacturer, string? Model, string? Cpu, int? CpuCores, long? MemoryBytes,
    string? Architecture, long? DiskTotalBytes, long? DiskFreeBytes);

public sealed record InventoryProgramDto(string? Name, string? Version, string? Publisher);

public sealed record InventoryRequest(InventoryHardwareDto? Hardware, List<InventoryProgramDto>? Software);

public sealed class AgentMtlsOptions
{
    public AgentMtlsMode Mode { get; set; } = AgentMtlsMode.Off;

    /// <summary>Directory of the agent CA (ca.crt, ca.key); created on first start. The key is stored there unencrypted (0600).</summary>
    public string CaDirectory { get; set; } = "pki";

    /// <summary>Validity of issued agent certificates.</summary>
    public int CertificateValidityDays { get; set; } = 365;

    /// <summary>The server asks the agent to renew this many days before expiry.</summary>
    public int RenewBeforeDays { get; set; } = 30;

    /// <summary>
    /// Name of the request header in which a trusted reverse proxy forwards the client certificate (URL-encoded PEM or Base64 DER),
    /// e.g. <c>X-Client-Cert</c>. Empty = off. Only honoured for peers listed in <c>Proxy:TrustedProxies</c>; the proxy must
    /// overwrite the header on every request and may also strip it from requests without a certificate.
    /// </summary>
    public string ForwardedCertificateHeader { get; set; } = "";
}

public enum AgentUpdateMode
{
    /// <summary>The server offers no updates.</summary>
    Off,

    /// <summary>Agents receive the highest available version (upgrades only).</summary>
    Latest,

    /// <summary>Agents receive exactly <see cref="AgentUpdateOptions.TargetVersion"/> (downgrades are possible).</summary>
    Pinned
}

public sealed class AgentUpdateOptions
{
    public AgentUpdateMode Mode { get; set; } = AgentUpdateMode.Off;

    /// <summary>Target version for <see cref="AgentUpdateMode.Pinned"/>, e.g. 0.4.1.</summary>
    public string TargetVersion { get; set; } = "";

    /// <summary>Share of devices (0–100) that are offered the update. Stable per device; raise gradually for a staged rollout.</summary>
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

/// <summary>Offer for an agent update; <c>Url</c> is relative to the server address.</summary>
public sealed record AgentUpdateOfferDto(string Version, string Sha256, long Size, string Url);

public sealed record HeartbeatResponse(
    IReadOnlyList<AgentJobDto> Jobs, int NextHeartbeatSeconds, AgentUpdateOfferDto? Update = null,
    bool CertificateRequested = false);

// ---- Verwaltung (Web-UI) ----

public sealed record CreateTokenRequest(string? Label, int? ValidHours, int? MaxUses = null);

public sealed record TokenDto(
    Guid Id, string Label, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? UsedAt, string Status,
    int MaxUses = 1, int UseCount = 0);

/// <summary>One-time response on creation: the only one that contains the plaintext token.</summary>
public sealed record TokenCreatedDto(Guid Id, string Label, string Token, DateTimeOffset ExpiresAt);

public sealed record CreateJobRequest(Guid DeviceId, string? Type, string? Payload);

public sealed record JobViewDto(
    Guid Id, Guid DeviceId, string Type, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, string? Output);
