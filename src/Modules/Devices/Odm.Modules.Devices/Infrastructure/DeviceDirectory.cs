using Microsoft.EntityFrameworkCore;
using Odm.Modules.Devices.Application;
using Odm.Modules.Devices.Domain;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.Devices.Infrastructure;

internal sealed class DeviceDirectory(DevicesDbContext db) : IDeviceDirectory
{
    public async Task<DeviceRef?> GetAsync(Guid deviceId, CancellationToken ct)
    {
        var device = await db.Devices.AsNoTracking().SingleOrDefaultAsync(d => d.Id == deviceId, ct);
        return device is null ? null : Map(device);
    }

    public async Task<IReadOnlyDictionary<Guid, DeviceRef>> GetManyAsync(
        IReadOnlyCollection<Guid> deviceIds, CancellationToken ct)
    {
        var ids = deviceIds.Distinct().ToList();
        var devices = await db.Devices.AsNoTracking().Where(d => ids.Contains(d.Id)).ToListAsync(ct);
        return devices.ToDictionary(d => d.Id, Map);
    }

    public Task<bool> GroupExistsAsync(Guid groupId, CancellationToken ct) =>
        db.Groups.AnyAsync(g => g.Id == groupId, ct);

    public async Task<IReadOnlyDictionary<Guid, string>> GetGroupNamesAsync(
        IReadOnlyCollection<Guid> groupIds, CancellationToken ct)
    {
        var ids = groupIds.Distinct().ToList();
        return await db.Groups.AsNoTracking().Where(g => ids.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, ct);
    }

    public async Task<IReadOnlyList<DeviceRef>> ResolveGroupAsync(Guid groupId, CancellationToken ct)
    {
        var group = await db.Groups.AsNoTracking().SingleOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null)
        {
            return [];
        }

        var devices = await db.Devices.AsNoTracking()
            .Where(GroupFilter.Build(group.MatchMode, group.Rules))
            .ToListAsync(ct);
        return devices.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<DeviceRef>> ResolveUserAsync(string userName, CancellationToken ct)
    {
        var lowered = userName.Trim().ToLower();
        var devices = await db.Devices.AsNoTracking()
            .Where(d => d.PrimaryUser != null && d.PrimaryUser.ToLower() == lowered)
            .ToListAsync(ct);
        return devices.Select(Map).ToList();
    }

    private static DeviceRef Map(Device d) => new(d.Id, d.Name, d.OsFamily.ToString(), d.OsName);
}
