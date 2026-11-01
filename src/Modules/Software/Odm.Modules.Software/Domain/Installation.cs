using Odm.SharedKernel;

namespace Odm.Modules.Software.Domain;

public enum InstallStatus
{
    /// <summary>Should be installed, job not yet queued.</summary>
    Pending = 0,

    /// <summary>Job is queued or running on the device.</summary>
    Running = 1,

    Installed = 2,
    Failed = 3,

    /// <summary>Uninstall succeeded; the entry is deleted at the next reconciliation.</summary>
    Removed = 4
}

/// <summary>Actual/desired state of a package on a device (exactly one entry per device and package).</summary>
public sealed class Installation : Entity
{
    public const int MaxAttempts = 3;
    public const int MaxMessageLength = 64000;

    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RunningTimeout = TimeSpan.FromHours(6);

    public Guid DeviceId { get; private set; }
    public Guid PackageId { get; private set; }
    public Guid TargetVersionId { get; private set; }
    public Guid? InstalledVersionId { get; private set; }

    /// <summary>Version for which the current or last job was issued.</summary>
    public Guid? JobVersionId { get; private set; }

    public Guid? JobId { get; private set; }
    public InstallStatus Status { get; private set; }

    /// <summary>The package should be removed from this device (the status then describes the uninstall).</summary>
    public bool Removal { get; private set; }
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

    /// <summary>Reconciles the state with the desired version (called on every reconciliation run).</summary>
    /// <param name="refreshAfter">For automatically updated repo packages: "install/update" again after this time.</param>
    public void Evaluate(Guid target, DateTimeOffset now, TimeSpan? refreshAfter = null)
    {
        TargetVersionId = target;

        if (Removal)
        {
            // The intent changed back to "install".
            Removal = false;
            Requeue(now, resetAttempts: true);
            return;
        }

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

    /// <summary>
    /// Reconciles with the intent "uninstall". Returns false if there is nothing to remove
    /// (never installed successfully): the entry can then be deleted.
    /// </summary>
    public bool EvaluateRemoval(DateTimeOffset now)
    {
        if (InstalledVersionId is not { } installed)
        {
            return Status == InstallStatus.Running; // laufende Installation abwarten
        }

        if (!Removal)
        {
            Removal = true;
            TargetVersionId = installed; // remove with the installed version (for PSADT the matching ZIP)
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

        return true;
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

        if (success && Removal)
        {
            Status = InstallStatus.Removed;
            InstalledVersionId = null;
            Attempts = 0;
        }
        else if (success)
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

    /// <summary>Manual restart after a failure.</summary>
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
