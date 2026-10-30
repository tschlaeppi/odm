using Odm.SharedKernel;

namespace Odm.Modules.Software.Domain;

public enum InstallStatus
{
    /// <summary>Soll installiert werden, Auftrag noch nicht eingereiht.</summary>
    Pending = 0,

    /// <summary>Auftrag ist eingereiht oder läuft auf dem Gerät.</summary>
    Running = 1,

    Installed = 2,
    Failed = 3
}

/// <summary>Ist-/Soll-Zustand eines Pakets auf einem Gerät (je Gerät und Paket genau ein Eintrag).</summary>
public sealed class Installation : Entity
{
    public const int MaxAttempts = 3;
    public const int MaxMessageLength = 2000;

    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RunningTimeout = TimeSpan.FromHours(6);

    public Guid DeviceId { get; private set; }
    public Guid PackageId { get; private set; }
    public Guid TargetVersionId { get; private set; }
    public Guid? InstalledVersionId { get; private set; }

    /// <summary>Version, für die der aktuelle bzw. letzte Auftrag erteilt wurde.</summary>
    public Guid? JobVersionId { get; private set; }

    public Guid? JobId { get; private set; }
    public InstallStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public string? Message { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Installation() { }

    public static Installation Create(Guid deviceId, Guid packageId, Guid targetVersionId, DateTimeOffset now) => new()
    {
        DeviceId = deviceId,
        PackageId = packageId,
        TargetVersionId = targetVersionId,
        Status = InstallStatus.Pending,
        UpdatedAt = now
    };

    /// <summary>Gleicht den Zustand mit der gewünschten Version ab (wird bei jedem Abgleichslauf aufgerufen).</summary>
    /// <param name="refreshAfter">Bei automatisch aktualisierten Repo-Paketen: nach dieser Zeit erneut "installieren/aktualisieren".</param>
    public void Evaluate(Guid target, DateTimeOffset now, TimeSpan? refreshAfter = null)
    {
        TargetVersionId = target;

        if (Status == InstallStatus.Installed && InstalledVersionId != target)
        {
            Requeue(now, resetAttempts: true);
        }
        else if (Status == InstallStatus.Installed && refreshAfter is { } refresh && now - UpdatedAt >= refresh)
        {
            Requeue(now, resetAttempts: true);
        }
        else if (Status == InstallStatus.Failed && JobVersionId != target)
        {
            Requeue(now, resetAttempts: true);
        }
        else if (Status == InstallStatus.Failed && Attempts < MaxAttempts && now - UpdatedAt >= RetryAfter)
        {
            Requeue(now, resetAttempts: false);
        }
        else if (Status == InstallStatus.Running && now - UpdatedAt >= RunningTimeout)
        {
            Requeue(now, resetAttempts: false);
        }
    }

    public void MarkQueued(Guid jobId, DateTimeOffset now)
    {
        Status = InstallStatus.Running;
        JobId = jobId;
        JobVersionId = TargetVersionId;
        UpdatedAt = now;
    }

    public void Complete(bool success, string? output, DateTimeOffset now)
    {
        UpdatedAt = now;
        Message = output is { Length: > MaxMessageLength } ? output[^MaxMessageLength..] : output;

        if (success)
        {
            Status = InstallStatus.Installed;
            InstalledVersionId = JobVersionId;
            Attempts = 0;
        }
        else
        {
            Status = InstallStatus.Failed;
            Attempts++;
        }
    }

    /// <summary>Manueller Neustart nach Fehlschlag.</summary>
    public void Retry(DateTimeOffset now) => Requeue(now, resetAttempts: true);

    private void Requeue(DateTimeOffset now, bool resetAttempts)
    {
        Status = InstallStatus.Pending;
        JobId = null;
        UpdatedAt = now;
        if (resetAttempts)
        {
            Attempts = 0;
        }
    }
}
