namespace Odm.SharedKernel.Contracts;

/// <summary>Vom Agent gemeldete Geräte-Informationen.</summary>
public sealed record DeviceReport(
    string Hostname,
    string OsFamily,
    string OsName,
    string OsVersion,
    string? AgentVersion);

/// <summary>
/// Schnittstelle des Devices-Moduls für andere Module (z. B. Agents).
/// So bleiben die Module entkoppelt: sie kennen nur diesen Vertrag, nicht die Implementierung.
/// </summary>
public interface IDeviceRegistry
{
    /// <summary>Legt das Gerät an oder liefert das bestehende Gerät mit gleichem Namen zurück.</summary>
    Task<Guid> RegisterAsync(DeviceReport report, CancellationToken ct);

    /// <summary>Aktualisiert OS-Informationen und "zuletzt gesehen". Unbekannte Geräte werden ignoriert.</summary>
    Task ReportAsync(Guid deviceId, DeviceReport report, CancellationToken ct);
}
