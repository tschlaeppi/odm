using Odm.SharedKernel;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.Agents.Domain;

public enum JobStatus
{
    Pending = 0,
    Delivered = 1,
    Succeeded = 2,
    Failed = 3
}

/// <summary>Unterstützte Auftragstypen. Weitere kommen mit der Softwareverteilung.</summary>
public static class JobTypes
{
    public const string Ping = AgentJobTypes.Ping;
    public const string InstallPsadt = AgentJobTypes.InstallPsadt;
    public const string InstallRepo = AgentJobTypes.InstallRepo;
    public const string OsUpdate = AgentJobTypes.OsUpdate;

    /// <summary>Alle Typen, die ein Agent kennt.</summary>
    public static readonly IReadOnlyCollection<string> All = [Ping, InstallPsadt, InstallRepo, OsUpdate];

    /// <summary>Typen, die Benutzer direkt in der Oberfläche anstossen dürfen (der Rest entsteht aus Zuweisungen).</summary>
    public static readonly IReadOnlyCollection<string> UserCreatable = [Ping];
}

/// <summary>Auftrag an einen Agent. Wird beim Heartbeat ausgeliefert, das Ergebnis beim nächsten gemeldet.</summary>
public sealed class AgentJob : Entity
{
    public const int MaxOutputLength = 8000;

    public Guid DeviceId { get; private set; }
    public string Type { get; private set; } = "";
    public string? Payload { get; private set; }
    public JobStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? Output { get; private set; }

    private AgentJob() { }

    public static AgentJob Create(Guid deviceId, string type, string? payload) => new()
    {
        DeviceId = deviceId,
        Type = type,
        Payload = payload,
        Status = JobStatus.Pending,
        CreatedAt = DateTimeOffset.UtcNow
    };

    public void MarkDelivered(DateTimeOffset now)
    {
        Status = JobStatus.Delivered;
        DeliveredAt = now;
    }

    /// <summary>Ergebnis übernehmen; nur für ausgelieferte Aufträge (verhindert doppelte Meldungen).</summary>
    public bool Complete(bool success, string? output, DateTimeOffset now)
    {
        if (Status != JobStatus.Delivered)
        {
            return false;
        }

        Status = success ? JobStatus.Succeeded : JobStatus.Failed;
        CompletedAt = now;
        Output = output is { Length: > MaxOutputLength } ? output[..MaxOutputLength] : output;
        return true;
    }
}
