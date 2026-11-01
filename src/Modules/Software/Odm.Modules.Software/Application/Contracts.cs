namespace Odm.Modules.Software.Application;

/// <summary>Settings of a package; which fields apply depends on the kind.</summary>
public sealed record PackageSpecDto(
    string? Identifier, string? Version, string? Scope, string? Source, string? Remote, string? Channel,
    bool Classic, string? Arguments, string? Platform = null);

public sealed record CreatePackageRequest(
    string? Name, string? Description, string? Kind, PackageSpecDto? Spec, List<Guid>? DependsOn = null);

/// <summary>DependsOn: null = unchanged, otherwise replaces the list of required packages.</summary>
public sealed record UpdatePackageRequest(
    string? Description, PackageSpecDto? Spec, string? Name = null, List<Guid>? DependsOn = null);

public sealed record PackageRefDto(Guid Id, string Name);

public sealed record VersionDto(Guid Id, string Version, long SizeBytes, string? Sha256, string? Notes, DateTimeOffset UploadedAt);

public sealed record PackageDto(
    Guid Id, string Name, string? Description, string Kind, DateTimeOffset CreatedAt,
    string? LatestVersion, int VersionCount, PackageSpecDto Spec);

public sealed record PackageDetailDto(
    Guid Id, string Name, string? Description, string Kind, DateTimeOffset CreatedAt,
    PackageSpecDto Spec, IReadOnlyList<VersionDto> Versions, IReadOnlyList<PackageRefDto>? DependsOn = null);

/// <summary>Tranche plan. Mode "Percent" (stages with percentages) or "Groups" (stages with a subgroup).</summary>
public sealed record RolloutStageDto(int AfterDays, int? Percent, Guid? GroupId);

public sealed record RolloutDto(string? Mode, IReadOnlyList<RolloutStageDto>? Stages);

/// <summary>State of the tranches: stages reached and (in percent mode) the released share.</summary>
public sealed record RolloutStatusDto(int StagesReached, int StagesTotal, int? ReleasedPercent);

public sealed record AssignmentRequest(
    Guid PackageId, string? TargetKind, string? Target, string? VersionPolicy, Guid? PinnedVersionId,
    RolloutDto? Rollout = null, string? Intent = null);

public sealed record UpdateAssignmentRequest(
    string? VersionPolicy, Guid? PinnedVersionId, RolloutDto? Rollout = null, string? Intent = null);

public sealed record AssignmentDto(
    Guid Id, Guid PackageId, string TargetKind, string Target, string? TargetName,
    string VersionPolicy, Guid? PinnedVersionId, DateTimeOffset CreatedAt,
    RolloutDto? Rollout = null, RolloutStatusDto? RolloutStatus = null, string Intent = "Install");

public sealed record ProfileItemRequest(Guid PackageId, string? VersionPolicy, Guid? PinnedVersionId);

public sealed record CreateProfileRequest(string? Name, string? Description, IReadOnlyList<ProfileItemRequest>? Items);

public sealed record UpdateProfileRequest(string? Description, IReadOnlyList<ProfileItemRequest>? Items);

public sealed record ProfileItemDto(
    Guid PackageId, string PackageName, string PackageKind, int Position, string VersionPolicy, Guid? PinnedVersionId);

public sealed record ProfileDto(Guid Id, string Name, string? Description, int ItemCount, DateTimeOffset CreatedAt);

public sealed record ProfileDetailDto(
    Guid Id, string Name, string? Description, DateTimeOffset CreatedAt, IReadOnlyList<ProfileItemDto> Items);

public sealed record ProfileAssignmentRequest(Guid ProfileId, string? TargetKind, string? Target, RolloutDto? Rollout);

public sealed record UpdateProfileAssignmentRequest(RolloutDto? Rollout);

public sealed record ProfileAssignmentDto(
    Guid Id, Guid ProfileId, string TargetKind, string Target, string? TargetName, DateTimeOffset CreatedAt,
    RolloutDto? Rollout, RolloutStatusDto? RolloutStatus);

public sealed record InstallationDto(
    Guid Id, Guid DeviceId, string? DeviceName, Guid PackageId, string PackageName,
    string? TargetVersion, string? InstalledVersion, string Status, int Attempts, string? Message,
    DateTimeOffset UpdatedAt, bool Removal = false, IReadOnlyList<string>? WaitingFor = null);

public sealed record InstallTotalsDto(
    int Installed, int Pending, int Running, int Failed, int Removing, int RemovalFailed);

public sealed record PackageStatsDto(Guid PackageId, string Name, int Installed, int InProgress, int Failed);

public sealed record FailureDto(
    Guid InstallationId, Guid DeviceId, string? DeviceName, Guid PackageId, string PackageName,
    bool Removal, int Attempts, string? Message, DateTimeOffset UpdatedAt);

public sealed record SoftwareStatsDto(
    int Packages, int Assignments, InstallTotalsDto Installations,
    IReadOnlyList<PackageStatsDto> PerPackage, IReadOnlyList<FailureDto> Failures);

public sealed record SecretDto(string Name, DateTimeOffset UpdatedAt);

public sealed record SetSecretRequest(string? Value);

public sealed record ResolveSecretsRequest(Guid JobId);

public sealed record ResolveSecretsResponse(IReadOnlyDictionary<string, string> Secrets);

/// <summary>Content of the "install-psadt" job (read by the agent).</summary>
public sealed record InstallPsadtPayload(
    Guid VersionId, string PackageName, string Version, string Sha256, long SizeBytes, int TimeoutMinutes,
    string? Arguments, bool Uninstall = false);

/// <summary>Content of the job "install-repo": installs or updates a package through the device's package manager.</summary>
public sealed record InstallRepoPayload(
    string Manager, string Identifier, string? Version, string? Scope, string? Source, string? Remote,
    string? Channel, bool Classic, string? Arguments, int TimeoutMinutes, bool Uninstall = false);

public sealed record ComplianceRowDto(
    Guid DeviceId, string DeviceName, Guid PackageId, string PackageName, string Reason,
    string? TargetVersion, string? InstalledVersion, int Attempts, DateTimeOffset UpdatedAt);

/// <summary>Installations in total, how many deviate, deviations per reason and the (filtered) rows.</summary>
public sealed record ComplianceDto(
    int Total, int Deviating, IReadOnlyDictionary<string, int> ByReason, IReadOnlyList<ComplianceRowDto> Rows);

/// <summary>
/// A package of a profile that applies to a device. Status = installation status, or without installation:
/// Scheduled (rollout stage not reached yet), NotApplicable (other platform/distribution), NoVersion (nothing uploaded / pinned version invalid).
/// </summary>
public sealed record DeviceProfileItemDto(
    int Position, Guid PackageId, string PackageName, string Status, string? TargetVersion, string? InstalledVersion,
    int Attempts, string? Message, DateTimeOffset? UpdatedAt);

/// <summary>A profile that applies to a device; Source = Device, User or Group (SourceName: user or group name).</summary>
public sealed record DeviceProfileDto(
    Guid ProfileId, string ProfileName, string Source, string? SourceName, IReadOnlyList<DeviceProfileItemDto> Items);
