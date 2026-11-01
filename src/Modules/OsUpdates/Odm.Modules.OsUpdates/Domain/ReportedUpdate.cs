using Odm.SharedKernel;

namespace Odm.Modules.OsUpdates.Domain;

/// <summary>An update from the operating system's own history, as reported by the agent with the inventory.</summary>
public sealed class ReportedUpdateEntry : Entity
{
    public const int MaxTitleLength = 300;

    public Guid DeviceId { get; private set; }
    public string Title { get; private set; } = "";
    public DateTimeOffset InstalledAt { get; private set; }

    private ReportedUpdateEntry() { }

    public static ReportedUpdateEntry Create(Guid deviceId, string title, DateTimeOffset installedAt) => new()
    {
        DeviceId = deviceId,
        Title = title.Length > MaxTitleLength ? title[..MaxTitleLength] : title,
        InstalledAt = installedAt
    };
}
