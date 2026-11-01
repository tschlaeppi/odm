using Odm.Agent.Os;

namespace Odm.Tests;

public class WindowsUpdaterTests
{
    [Fact]
    public void Security_and_critical_updates_count_as_security()
    {
        Assert.True(WindowsUpdater.IsSecurity(["Updates", "Security Updates"]));
        Assert.True(WindowsUpdater.IsSecurity(["Critical Updates"]));
        Assert.False(WindowsUpdater.IsSecurity(["Drivers", "Definition Updates"]));
        Assert.False(WindowsUpdater.IsSecurity([]));
    }
}
