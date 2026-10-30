namespace Odm.SharedKernel.Contracts;

/// <summary>Auftragstypen, die zwischen Modulen und Agent vereinbart sind.</summary>
public static class AgentJobTypes
{
    public const string Ping = "ping";
    public const string InstallPsadt = "install-psadt";
    public const string InstallRepo = "install-repo";
    public const string OsUpdate = "os-update";
}

public sealed record JobCompletion(Guid JobId, Guid DeviceId, string Type, bool Success, string? Output);

/// <summary>Reiht Aufträge für Agents ein (implementiert vom Agents-Modul).</summary>
public interface IJobQueue
{
    /// <summary>Gibt null zurück, wenn für das Gerät (noch) kein Agent registriert ist.</summary>
    Task<Guid?> EnqueueAsync(Guid deviceId, string type, string? payload, CancellationToken ct);
}

/// <summary>Wird benachrichtigt, wenn ein Agent das Ergebnis eines Auftrags gemeldet hat.</summary>
public interface IJobResultListener
{
    Task OnJobCompletedAsync(JobCompletion completion, CancellationToken ct);
}
