using Odm.SharedKernel;

namespace Odm.Modules.OsUpdates.Domain;

public enum AssignmentScope
{
    Policy = 0,
    Window = 1
}

/// <summary>Reihenfolge = Vorrang: ein direkt zugewiesenes Gerät geht der Gruppe vor.</summary>
public enum TargetKind
{
    Device = 0,
    Group = 1
}

/// <summary>Weist eine Richtlinie oder ein Wartungsfenster einem Gerät oder einer Gruppe zu.</summary>
public sealed class OsAssignment : Entity
{
    public AssignmentScope Scope { get; private set; }
    public Guid ParentId { get; private set; }
    public TargetKind TargetKind { get; private set; }

    /// <summary>Geräte- bzw. Gruppen-ID im Format "D", kleingeschrieben.</summary>
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
