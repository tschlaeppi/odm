using Microsoft.EntityFrameworkCore;
using Odm.Modules.Devices.Domain;
using Odm.SharedKernel;

namespace Odm.Modules.Devices.Application;

public sealed record DeviceDto(
    Guid Id,
    string Name,
    string OsFamily,
    string OsName,
    string OsVersion,
    string? PrimaryUser,
    string? OrgUnit,
    string? CostCenter,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? LastSeenAt,
    string? AgentVersion)
{
    public static DeviceDto From(Device d) => new(
        d.Id, d.Name, d.OsFamily.ToString(), d.OsName, d.OsVersion,
        d.PrimaryUser, d.PrimaryUserOrgUnit, d.CostCenter, d.RegisteredAt, d.LastSeenAt, d.AgentVersion);
}

public sealed record AssignmentRequest(string? PrimaryUser, string? OrgUnit, string? CostCenter);

public static class DeviceQueries
{
    public static async Task<PagedResult<DeviceDto>> PageAsync(
        IQueryable<Device> query, int? skip, int? take, CancellationToken ct)
    {
        var offset = Math.Max(skip ?? 0, 0);
        var limit = Math.Clamp(take ?? 100, 1, 500);

        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(d => d.Name).Skip(offset).Take(limit).ToListAsync(ct);

        return new PagedResult<DeviceDto>(rows.Select(DeviceDto.From).ToList(), total);
    }
}
