namespace Odm.Modules.Software.Application;

/// <summary>Einstellungen eines Pakets; welche Felder gelten, hängt von der Art ab.</summary>
public sealed record PackageSpecDto(
    string? Identifier, string? Version, string? Scope, string? Source, string? Remote, string? Channel,
    bool Classic, string? Arguments);

public sealed record CreatePackageRequest(string? Name, string? Description, string? Kind, PackageSpecDto? Spec);

public sealed record UpdatePackageRequest(string? Description, PackageSpecDto? Spec);

public sealed record VersionDto(Guid Id, string Version, long SizeBytes, string? Sha256, string? Notes, DateTimeOffset UploadedAt);

public sealed record PackageDto(
    Guid Id, string Name, string? Description, string Kind, DateTimeOffset CreatedAt,
    string? LatestVersion, int VersionCount, PackageSpecDto Spec);

public sealed record PackageDetailDto(
    Guid Id, string Name, string? Description, string Kind, DateTimeOffset CreatedAt,
    PackageSpecDto Spec, IReadOnlyList<VersionDto> Versions);

public sealed record AssignmentRequest(
    Guid PackageId, string? TargetKind, string? Target, string? VersionPolicy, Guid? PinnedVersionId);

public sealed record UpdateAssignmentRequest(string? VersionPolicy, Guid? PinnedVersionId);

public sealed record AssignmentDto(
    Guid Id, Guid PackageId, string TargetKind, string Target, string? TargetName,
    string VersionPolicy, Guid? PinnedVersionId, DateTimeOffset CreatedAt);

public sealed record InstallationDto(
    Guid Id, Guid DeviceId, string? DeviceName, Guid PackageId, string PackageName,
    string? TargetVersion, string? InstalledVersion, string Status, int Attempts, string? Message,
    DateTimeOffset UpdatedAt);

public sealed record SecretDto(string Name, DateTimeOffset UpdatedAt);

public sealed record SetSecretRequest(string? Value);

public sealed record ResolveSecretsRequest(Guid JobId);

public sealed record ResolveSecretsResponse(IReadOnlyDictionary<string, string> Secrets);

/// <summary>Inhalt des Auftrags "install-psadt" (vom Agent gelesen).</summary>
public sealed record InstallPsadtPayload(
    Guid VersionId, string PackageName, string Version, string Sha256, long SizeBytes, int TimeoutMinutes,
    string? Arguments);

/// <summary>Inhalt des Auftrags "install-repo": installiert bzw. aktualisiert ein Paket über den Paketmanager des Geräts.</summary>
public sealed record InstallRepoPayload(
    string Manager, string Identifier, string? Version, string? Scope, string? Source, string? Remote,
    string? Channel, bool Classic, string? Arguments, int TimeoutMinutes);
