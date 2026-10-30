using Odm.SharedKernel;

namespace Odm.Modules.Devices.Domain;

public enum OsFamily
{
    Windows = 0,
    Linux = 1
}

public sealed class Device : Entity
{
    public string Name { get; private set; } = "";
    public OsFamily OsFamily { get; private set; }
    public string OsName { get; private set; } = "";
    public string OsVersion { get; private set; } = "";

    /// <summary>Hauptbenutzer (Verzeichnisbenutzer, nicht zwingend ein ODM-Login).</summary>
    public string? PrimaryUser { get; private set; }

    /// <summary>Organisationseinheit des Hauptbenutzers (später aus dem Verzeichnis synchronisiert).</summary>
    public string? PrimaryUserOrgUnit { get; private set; }

    public string? CostCenter { get; private set; }
    public DateTimeOffset RegisteredAt { get; private set; }
    public DateTimeOffset? LastSeenAt { get; private set; }
    public string? AgentVersion { get; private set; }

    private Device() { }

    public static Device Register(string name, OsFamily family, string osName, string osVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new Device
        {
            Name = name.Trim(),
            OsFamily = family,
            OsName = osName,
            OsVersion = osVersion,
            RegisteredAt = DateTimeOffset.UtcNow
        };
    }

    public void UpdateAssignment(string? primaryUser, string? primaryUserOrgUnit, string? costCenter)
    {
        PrimaryUser = Clean(primaryUser);
        PrimaryUserOrgUnit = Clean(primaryUserOrgUnit);
        CostCenter = Clean(costCenter);
    }

    /// <summary>Übernimmt einen geänderten Rechnernamen (Gross-/Kleinschreibung zählt als Änderung).</summary>
    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public void MarkSeen() => LastSeenAt = DateTimeOffset.UtcNow;

    /// <summary>Übernimmt die vom Agent gemeldeten Werte.</summary>
    public void ApplyReport(OsFamily family, string osName, string osVersion, string? agentVersion)
    {
        OsFamily = family;
        OsName = osName;
        OsVersion = osVersion;
        AgentVersion = agentVersion;
        MarkSeen();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
