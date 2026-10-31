using Odm.Modules.Software.Domain;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.Software.Application;

/// <summary>Rough classification of the Linux distribution based on the reported OS name.</summary>
public static class DistroFamily
{
    private static readonly string[] Deb = ["ubuntu", "debian", "mint", "pop!_os", "pop_os", "raspbian", "kali", "elementary", "zorin"];
    private static readonly string[] Rpm = ["red hat", "rhel", "centos", "fedora", "rocky", "alma", "suse", "oracle", "amazon"];

    private static bool Matches(string osName, string[] keywords) =>
        keywords.Any(k => osName.Contains(k, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Does the package kind fit the device? An unknown distribution is allowed; the agent then reports clearly
    /// if the package manager is missing.
    /// </summary>
    public static bool Applies(PackageKind kind, PackageSpec spec, DeviceRef device)
    {
        var windows = string.Equals(device.OsFamily, "Windows", StringComparison.OrdinalIgnoreCase);
        if (kind.RunsOnWindows(spec))
        {
            return windows;
        }

        if (windows)
        {
            return false;
        }

        return kind switch
        {
            PackageKind.Psadt => true, // Linux PSADT: needs pwsh, the agent reports clearly if it is missing
            PackageKind.Apt => !Matches(device.OsName, Rpm),
            PackageKind.Rpm => !Matches(device.OsName, Deb),
            _ => true // Flatpak, Snap
        };
    }
}
