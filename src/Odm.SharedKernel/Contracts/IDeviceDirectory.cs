namespace Odm.SharedKernel.Contracts;

public sealed record DeviceRef(Guid Id, string Name, string OsFamily, string OsName);

/// <summary>
/// Read access to devices for other modules (e.g. Software): who belongs to a group, who owns a device.
/// </summary>
public interface IDeviceDirectory
{
    Task<DeviceRef?> GetAsync(Guid deviceId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, DeviceRef>> GetManyAsync(IReadOnlyCollection<Guid> deviceIds, CancellationToken ct);

    Task<bool> GroupExistsAsync(Guid groupId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, string>> GetGroupNamesAsync(IReadOnlyCollection<Guid> groupIds, CancellationToken ct);

    /// <summary>Aktuelle Mitglieder einer dynamischen Gruppe.</summary>
    Task<IReadOnlyList<DeviceRef>> ResolveGroupAsync(Guid groupId, CancellationToken ct);

    /// <summary>Devices whose primary user is the given user.</summary>
    Task<IReadOnlyList<DeviceRef>> ResolveUserAsync(string userName, CancellationToken ct);
}
