using Odm.Modules.Software.Application;
using Odm.Modules.Software.Domain;
using Odm.SharedKernel.Contracts;

namespace Odm.Tests;

public class DistroFamilyTests
{
    private static DeviceRef Device(string family, string os) => new(Guid.NewGuid(), "dev", family, os);

    [Theory]
    [InlineData(PackageKind.Winget, "Windows", "Windows 11 Pro", true)]
    [InlineData(PackageKind.Chocolatey, "Windows", "Windows Server 2022", true)]
    [InlineData(PackageKind.Psadt, "Windows", "Windows 10", true)]
    [InlineData(PackageKind.Winget, "Linux", "Ubuntu", false)]
    [InlineData(PackageKind.Psadt, "Linux", "Ubuntu", false)]
    [InlineData(PackageKind.Flatpak, "Windows", "Windows 11 Pro", false)]
    [InlineData(PackageKind.Snap, "Windows", "Windows 11 Pro", false)]
    [InlineData(PackageKind.Apt, "Windows", "Windows 11 Pro", false)]
    public void Windows_and_linux_split(PackageKind kind, string family, string os, bool expected) =>
        Assert.Equal(expected, DistroFamily.Applies(kind, Device(family, os)));

    [Theory]
    [InlineData(PackageKind.Apt, "Ubuntu", true)]
    [InlineData(PackageKind.Apt, "Debian GNU/Linux", true)]
    [InlineData(PackageKind.Apt, "Linux Mint", true)]
    [InlineData(PackageKind.Apt, "Red Hat Enterprise Linux", false)]
    [InlineData(PackageKind.Apt, "Rocky Linux", false)]
    [InlineData(PackageKind.Rpm, "Rocky Linux", true)]
    [InlineData(PackageKind.Rpm, "Fedora Linux", true)]
    [InlineData(PackageKind.Rpm, "Ubuntu", false)]
    [InlineData(PackageKind.Rpm, "Linux Mint", false)]
    [InlineData(PackageKind.Apt, "Arch Linux", true)] // unknown: allow, the agent reports a clear error
    [InlineData(PackageKind.Rpm, "Arch Linux", true)]
    [InlineData(PackageKind.Flatpak, "Ubuntu", true)]
    [InlineData(PackageKind.Snap, "Rocky Linux", true)]
    public void Linux_distribution_families(PackageKind kind, string os, bool expected) =>
        Assert.Equal(expected, DistroFamily.Applies(kind, Device("Linux", os)));
}
