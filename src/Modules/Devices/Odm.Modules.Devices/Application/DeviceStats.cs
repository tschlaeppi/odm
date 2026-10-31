namespace Odm.Modules.Devices.Application;

public sealed record CountDto(string Name, int Count);

public sealed record SilentDeviceDto(
    Guid Id, string Name, string OsName, DateTimeOffset? LastSeenAt, DateTimeOffset RegisteredAt);

/// <summary>Key figures about the devices (dashboard).</summary>
public sealed record DeviceStatsDto(
    int Total, int Online, int Silent, int NeverSeen,
    IReadOnlyList<CountDto> ByOs, IReadOnlyList<CountDto> ByAgentVersion,
    IReadOnlyList<SilentDeviceDto> SilentDevices);
