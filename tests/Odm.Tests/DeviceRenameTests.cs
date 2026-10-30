using Odm.Modules.Devices.Domain;
using Xunit;

namespace Odm.Tests;

public class DeviceRenameTests
{
    [Fact]
    public void Rename_changes_name_and_trims()
    {
        var device = Device.Register("PC-ALT", OsFamily.Windows, "Windows 11", "23H2");
        device.Rename("  PC-NEU ");
        Assert.Equal("PC-NEU", device.Name);
    }

    [Fact]
    public void Rename_rejects_empty_names()
    {
        var device = Device.Register("PC-ALT", OsFamily.Linux, "Ubuntu", "24.04");
        Assert.Throws<ArgumentException>(() => device.Rename(" "));
        Assert.Equal("PC-ALT", device.Name);
    }
}
