namespace Odm.SharedKernel.Contracts;

/// <summary>Alert types known to the notification module.</summary>
public static class AlertTypes
{
    public const string InstallationFailed = "installation-failed";
    public const string DeviceSilent = "device-silent";
    public const string UpdateRunFailed = "update-run-failed";
    public const string CertificateExpiring = "certificate-expiring";

    public static readonly string[] All = [InstallationFailed, DeviceSilent, UpdateRunFailed, CertificateExpiring];
}

/// <summary>
/// Something worth an alert. <paramref name="Key"/> identifies this occurrence (e.g. installation + time of failure):
/// an alert is sent once per key, a new failure produces a new key.
/// </summary>
public sealed record AlertCandidate(string Type, string Key, Guid? DeviceId, string Subject, string Detail);

/// <summary>Implemented by modules that know about alert-worthy situations (read only).</summary>
public interface IAlertSource
{
    Task<IReadOnlyList<AlertCandidate>> GetAsync(DateTimeOffset now, CancellationToken ct);
}
