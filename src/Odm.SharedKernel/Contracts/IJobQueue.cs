namespace Odm.SharedKernel.Contracts;

/// <summary>Job types agreed between modules and agent.</summary>
public static class AgentJobTypes
{
    public const string Ping = "ping";
    public const string InstallPsadt = "install-psadt";
    public const string InstallRepo = "install-repo";
    public const string OsUpdate = "os-update";
}

public sealed record JobCompletion(Guid JobId, Guid DeviceId, string Type, bool Success, string? Output);

/// <summary>Queues jobs for agents (implemented by the Agents module).</summary>
public interface IJobQueue
{
    /// <summary>Returns null if no agent is (yet) registered for the device.</summary>
    Task<Guid?> EnqueueAsync(Guid deviceId, string type, string? payload, CancellationToken ct);
}

/// <summary>Notified when an agent has reported the result of a job.</summary>
public interface IJobResultListener
{
    Task OnJobCompletedAsync(JobCompletion completion, CancellationToken ct);
}
