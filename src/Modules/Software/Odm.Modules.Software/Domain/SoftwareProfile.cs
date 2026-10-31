using Odm.SharedKernel;

namespace Odm.Modules.Software.Domain;

/// <summary>Software profile: packages that are installed in the given order.</summary>
public sealed class SoftwareProfile : Entity
{
    public string Name { get; private set; } = "";
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private SoftwareProfile() { }

    public static SoftwareProfile Create(string name, string? description) => new()
    {
        Name = name.Trim(),
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
        CreatedAt = DateTimeOffset.UtcNow
    };

    public void Update(string? description) =>
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}

/// <summary>Entry of a profile. <see cref="Position"/> determines the installation order (0 = first).</summary>
public sealed class ProfileItem : Entity
{
    public Guid ProfileId { get; private set; }
    public Guid PackageId { get; private set; }
    public int Position { get; private set; }
    public VersionPolicy VersionPolicy { get; private set; }
    public Guid? PinnedVersionId { get; private set; }

    private ProfileItem() { }

    public static ProfileItem Create(Guid profileId, Guid packageId, int position, VersionPolicy policy, Guid? pinnedVersionId) => new()
    {
        ProfileId = profileId,
        PackageId = packageId,
        Position = position,
        VersionPolicy = policy,
        PinnedVersionId = policy == VersionPolicy.Pinned ? pinnedVersionId : null
    };
}

/// <summary>Assignment of an entire profile to a device, a user or a group.</summary>
public sealed class ProfileAssignment : Entity
{
    public Guid ProfileId { get; private set; }
    public TargetKind TargetKind { get; private set; }
    public string Target { get; private set; } = "";
    public string? RolloutJson { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private ProfileAssignment() { }

    public static ProfileAssignment Create(Guid profileId, TargetKind kind, string target, RolloutPlan? rollout) => new()
    {
        ProfileId = profileId,
        TargetKind = kind,
        Target = target.Trim().ToLowerInvariant(),
        RolloutJson = rollout?.ToJson(),
        CreatedAt = DateTimeOffset.UtcNow
    };

    public void SetRollout(RolloutPlan? rollout) => RolloutJson = rollout?.ToJson();
}
