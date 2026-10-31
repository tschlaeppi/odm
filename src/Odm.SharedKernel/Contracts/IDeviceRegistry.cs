namespace Odm.SharedKernel.Contracts;

/// <summary>Device information reported by the agent.</summary>
public sealed record DeviceReport(
    string Hostname,
    string OsFamily,
    string OsName,
    string OsVersion,
    string? AgentVersion);

/// <summary>
/// Interface of the Devices module for other modules (e.g. Agents).
/// This keeps the modules decoupled: they only know this contract, not the implementation.
/// </summary>
public interface IDeviceRegistry
{
    /// <summary>Creates the device or returns the existing device with the same name.</summary>
    Task<Guid> RegisterAsync(DeviceReport report, CancellationToken ct);

    /// <summary>Updates OS information and "last seen". Unknown devices are ignored.</summary>
    Task ReportAsync(Guid deviceId, DeviceReport report, CancellationToken ct);
}
