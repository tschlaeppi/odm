using Odm.Modules.Software.Domain;

namespace Odm.Modules.Software.Application;

/// <summary>Decides whether an installation deviates from what the assignments demand.</summary>
public static class ComplianceRules
{
    public const string Failed = "Failed";
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string VersionMismatch = "VersionMismatch";
    public const string RemovalPending = "RemovalPending";
    public const string RemovalFailed = "RemovalFailed";

    /// <summary>The deviation reason, or null if the installation is as desired.</summary>
    public static string? Classify(InstallStatus status, bool removal, Guid? installedVersionId, Guid targetVersionId)
    {
        if (removal)
        {
            return status switch
            {
                InstallStatus.Removed => null,
                InstallStatus.Failed => RemovalFailed,
                _ => RemovalPending,
            };
        }

        return status switch
        {
            InstallStatus.Failed => Failed,
            InstallStatus.Pending => Pending,
            InstallStatus.Running => Running,
            InstallStatus.Installed when installedVersionId != targetVersionId => VersionMismatch,
            _ => null,
        };
    }
}
