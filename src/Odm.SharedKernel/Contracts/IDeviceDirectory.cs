namespace Odm.SharedKernel.Contracts;

public sealed record DeviceRef(Guid Id, string Name, string OsFamily, string OsName);

/// <summary>
/// Lesender Zugriff auf Geräte für andere Module (z. B. Software): wer gehört zu einer Gruppe, wem gehört ein Gerät.
/// </summary>
public interface IDeviceDirectory
{
    Task<DeviceRef?> GetAsync(Guid deviceId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, DeviceRef>> GetManyAsync(IReadOnlyCollection<Guid> deviceIds, CancellationToken ct);

    Task<bool> GroupExistsAsync(Guid groupId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, string>> GetGroupNamesAsync(IReadOnlyCollection<Guid> groupIds, CancellationToken ct);

    /// <summary>Aktuelle Mitglieder einer dynamischen Gruppe.</summary>
    Task<IReadOnlyList<DeviceRef>> ResolveGroupAsync(Guid groupId, CancellationToken ct);

    /// <summary>Geräte, deren Hauptbenutzer der angegebene Benutzer ist.</summary>
    Task<IReadOnlyList<DeviceRef>> ResolveUserAsync(string userName, CancellationToken ct);
}
