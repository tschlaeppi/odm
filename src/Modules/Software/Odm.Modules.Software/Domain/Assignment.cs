using Odm.SharedKernel;

namespace Odm.Modules.Software.Domain;

/// <summary>Reihenfolge = Vorrang, wenn dasselbe Paket einem Gerät mehrfach zugewiesen ist (Gerät vor Benutzer vor Gruppe).</summary>
public enum TargetKind
{
    Device = 0,
    User = 1,
    Group = 2
}

public enum VersionPolicy
{
    /// <summary>Immer die neueste hochgeladene Version (automatische Verteilung neuer Versionen).</summary>
    Latest = 0,

    /// <summary>Feste Version; ein Wechsel erfolgt bewusst durch Ändern der Zuweisung (manuell).</summary>
    Pinned = 1
}

public enum AssignmentIntent
{
    /// <summary>Das Paket soll auf den Zielgeräten installiert sein.</summary>
    Install = 0,

    /// <summary>Das Paket soll von den Zielgeräten entfernt werden (nur wo es von ODM installiert wurde).</summary>
    Uninstall = 1
}

/// <summary>Soll-Zustand: "dieses Paket gehört auf (bzw. weg von) diese(n) Geräte(n)".</summary>
public sealed class Assignment : Entity
{
    public Guid PackageId { get; private set; }
    public TargetKind TargetKind { get; private set; }

    /// <summary>Geräte-/Gruppen-ID (kleingeschrieben, Format "D") bzw. Benutzername (kleingeschrieben).</summary>
    public string Target { get; private set; } = "";

    public VersionPolicy VersionPolicy { get; private set; }
    public AssignmentIntent Intent { get; private set; }
    public Guid? PinnedVersionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Optionaler Tranchenplan (JSON); Start ist <see cref="CreatedAt"/>.</summary>
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
