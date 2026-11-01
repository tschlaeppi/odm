using Odm.Agent.Os;

namespace Odm.Tests;

public class LinuxUpdaterTitleTests
{
    [Fact]
    public void Apt_install_line_becomes_name_with_versions()
    {
        Assert.Equal(
            "libssl3 3.0.2-1 → 3.0.2-2",
            LinuxUpdater.AptTitle("Inst libssl3 [3.0.2-1] (3.0.2-2 Ubuntu:22.04/jammy-security [amd64])"));
    }

    [Fact]
    public void New_package_has_no_old_version()
    {
        Assert.Equal(
            "linux-image-6.8 6.8.0-50",
            LinuxUpdater.AptTitle("Inst linux-image-6.8 (6.8.0-50 Ubuntu:24.04/noble-updates [amd64])"));
    }

    [Fact]
    public void Line_without_versions_falls_back_to_name()
    {
        Assert.Equal("foo", LinuxUpdater.AptTitle("Inst foo"));
    }
}
