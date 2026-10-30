using Odm.Modules.Software.Domain;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.Software.Application;

/// <summary>Grobe Einordnung der Linux-Distribution anhand des gemeldeten OS-Namens.</summary>
public static class DistroFamily
{
    private static readonly string[] Deb = ["ubuntu", "debian", "mint", "pop!_os", "pop_os", "raspbian", "kali", "elementary", "zorin"];
    private static readonly string[] Rpm = ["red hat", "rhel", "centos", "fedora", "rocky", "alma", "suse", "oracle", "amazon"];

    private static bool Matches(string osName, string[] keywords) =>
        keywords.Any(k => osName.Contains(k, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Passt die Paketart zum Gerät? Bei unbekannter Distribution wird zugelassen; der Agent meldet dann klar,
    /// falls der Paketmanager fehlt.
    /// </summary>
    public static bool Applies(PackageKind kind, DeviceRef device)
    {
        var windows = string.Equals(device.OsFamily, "Windows", StringComparison.OrdinalIgnoreCase);
        if (kind.IsWindowsOnly())
        {
            return windows;
        }

        if (windows)
        {
            return false;
        }

        return kind switch
        {
            PackageKind.Apt => !Matches(device.OsName, Rpm),
            PackageKind.Rpm => !Matches(device.OsName, Deb),
            _ => true // Flatpak, Snap
        };
    }
}
