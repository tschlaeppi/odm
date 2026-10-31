using Odm.SharedKernel;

namespace Odm.Modules.OsUpdates.Domain;

public enum AssignmentScope
{
    Policy = 0,
    Window = 1
}

/// <summary>Order = precedence: a directly assigned device takes precedence over the group.</summary>
public enum TargetKind
{
    Device = 0,
    Group = 1
}

/// <summary>Assigns a policy or a maintenance window to a device or a group.</summary>
public sealed class OsAssignment : Entity
{
    public AssignmentScope Scope { get; private set; }
    public Guid ParentId { get; private set; }
    public TargetKind TargetKind { get; private set; }

    /// <summary>Device or group ID in format "D", lowercase.</summary>
    public string Target { get; private set; } = "";

    public DateTimeOffset CreatedAt { get; private set; }

    private OsAssignment() { }

    public static OsAssignment Create(AssignmentScope scope, Guid parentId, TargetKind kind, Guid target) => new()
    {
        Scope = scope,
        ParentId = parentId,
        TargetKind = kind,
        Target = target.ToString("D"),
        CreatedAt = DateTimeOffset.UtcNow
    };
}
