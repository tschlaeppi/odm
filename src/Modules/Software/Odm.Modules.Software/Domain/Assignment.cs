using Odm.SharedKernel;

namespace Odm.Modules.Software.Domain;

/// <summary>Order = precedence when the same package is assigned to a device several times (device before user before group).</summary>
public enum TargetKind
{
    Device = 0,
    User = 1,
    Group = 2
}

public enum VersionPolicy
{
    /// <summary>Always the latest uploaded version (automatic rollout of new versions).</summary>
    Latest = 0,

    /// <summary>Pinned version; changing it is a deliberate (manual) change of the assignment.</summary>
    Pinned = 1
}

public enum AssignmentIntent
{
    /// <summary>The package should be installed on the target devices.</summary>
    Install = 0,

    /// <summary>The package should be removed from the target devices (only where ODM installed it).</summary>
    Uninstall = 1
}

/// <summary>Desired state: "this package belongs on (or off) these device(s)".</summary>
public sealed class Assignment : Entity
{
    public Guid PackageId { get; private set; }
    public TargetKind TargetKind { get; private set; }

    /// <summary>Device/group ID (lowercase, format "D") or user name (lowercase).</summary>
    public string Target { get; private set; } = "";

    public VersionPolicy VersionPolicy { get; private set; }
    public AssignmentIntent Intent { get; private set; }
    public Guid? PinnedVersionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Optional tranche plan (JSON); the start is <see cref="CreatedAt"/>.</summary>
    public string? RolloutJson { get; private set; }

    private Assignment() { }

    public static Assignment Create(
        Guid packageId, TargetKind kind, string target, VersionPolicy policy, Guid? pinnedVersionId,
        RolloutPlan? rollout = null, AssignmentIntent intent = AssignmentIntent.Install) => new()
    {
        PackageId = packageId,
        TargetKind = kind,
        Target = target.Trim().ToLowerInvariant(),
        VersionPolicy = policy,
        Intent = intent,
        PinnedVersionId = policy == VersionPolicy.Pinned ? pinnedVersionId : null,
        RolloutJson = rollout?.ToJson(),
        CreatedAt = DateTimeOffset.UtcNow
    };

    public void ChangePolicy(VersionPolicy policy, Guid? pinnedVersionId)
    {
        VersionPolicy = policy;
        PinnedVersionId = policy == VersionPolicy.Pinned ? pinnedVersionId : null;
    }

    public void SetIntent(AssignmentIntent intent) => Intent = intent;

    public void SetRollout(RolloutPlan? rollout) => RolloutJson = rollout?.ToJson();
}
