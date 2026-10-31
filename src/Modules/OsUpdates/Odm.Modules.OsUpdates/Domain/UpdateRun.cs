using Odm.SharedKernel;

namespace Odm.Modules.OsUpdates.Domain;

public enum RunStatus
{
    Queued = 0,
    Succeeded = 1,
    Failed = 2
}

/// <summary>An update run on a device, either in a maintenance window or triggered manually.</summary>
public sealed class UpdateRun : Entity
{
    public const int MaxMessageLength = 2000;

    public Guid DeviceId { get; private set; }

    /// <summary>Guid.Empty for manually triggered runs.</summary>
    public Guid WindowId { get; private set; }

    /// <summary>Start of the window; together with device and window this prevents duplicate runs.</summary>
    public DateTimeOffset OccurrenceStart { get; private set; }

    public Guid JobId { get; private set; }
    public RunStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public int? Installed { get; private set; }
    public int? Failed { get; private set; }
    public bool RebootRequired { get; private set; }
    public bool Rebooting { get; private set; }
    public string? Message { get; private set; }

    private UpdateRun() { }

    public static UpdateRun Create(Guid deviceId, Guid windowId, DateTimeOffset occurrenceStart, Guid jobId, DateTimeOffset now) => new()
    {
        DeviceId = deviceId,
        WindowId = windowId,
        OccurrenceStart = occurrenceStart,
        JobId = jobId,
        Status = RunStatus.Queued,
        CreatedAt = now
    };

    public void Complete(
        bool success, int? installed, int? failed, bool rebootRequired, bool rebooting, string? message, DateTimeOffset now)
    {
        Status = success ? RunStatus.Succeeded : RunStatus.Failed;
        CompletedAt = now;
        Installed = installed;
        Failed = failed;
        RebootRequired = rebootRequired;
        Rebooting = rebooting;
        Message = message is { Length: > MaxMessageLength } ? message[..MaxMessageLength] : message;
    }
}
