namespace Odm.SharedKernel.Contracts;

/// <summary>An update the operating system itself lists as installed (independent of ODM).</summary>
public sealed record ReportedUpdate(string Title, DateTimeOffset InstalledAt);

/// <summary>Stores the update history an agent reads from the operating system (implemented by the OsUpdates module).</summary>
public interface IOsUpdateHistory
{
    /// <summary>Replaces the stored operating system history of a device by the new snapshot.</summary>
    Task ReplaceReportedAsync(Guid deviceId, IReadOnlyList<ReportedUpdate> updates, CancellationToken ct);
}
