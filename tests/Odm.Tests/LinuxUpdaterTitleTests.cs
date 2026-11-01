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

public class DpkgHistoryTests
{
    [Fact]
    public void Upgrade_line_is_parsed_like_an_update_run_title()
    {
        var update = Odm.Agent.Inventory.InventoryCollector.ParseDpkgLine(
            "2024-05-01 06:12:33 upgrade libssl3:amd64 3.0.2-1 3.0.2-2", "amd64");
        Assert.NotNull(update);
        Assert.Equal("libssl3 3.0.2-1 → 3.0.2-2", update.Title);
        Assert.Equal(new DateTime(2024, 5, 1, 6, 12, 33), update.InstalledAt.LocalDateTime);
    }

    [Fact]
    public void Foreign_architecture_is_kept()
    {
        var update = Odm.Agent.Inventory.InventoryCollector.ParseDpkgLine(
            "2024-05-01 06:12:33 upgrade libc6:i386 2.35-1 2.35-2", "amd64");
        Assert.Equal("libc6:i386 2.35-1 → 2.35-2", update?.Title);
    }

    [Theory]
    [InlineData("2024-05-01 06:12:33 install foo:amd64 <none> 1.0")]
    [InlineData("2024-05-01 06:12:33 status installed foo:amd64 1.0")]
    [InlineData("not a log line")]
    public void Other_lines_are_ignored(string line) =>
        Assert.Null(Odm.Agent.Inventory.InventoryCollector.ParseDpkgLine(line, "amd64"));
}
