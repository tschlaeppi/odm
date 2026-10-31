using Odm.Agent.Inventory;
using Odm.Modules.Devices.Domain;
using Xunit;

namespace Odm.Tests;

public class InventoryTests
{
    [Fact]
    public void Dpkg_output_keeps_only_installed_packages()
    {
        const string output =
            "bash\t5.2.21-2\tinstall ok installed\n" +
            "oldpkg\t1.0\tdeinstall ok config-files\n" +
            "curl\t8.5.0-2\tinstall ok installed\n" +
            "bash\t5.2.21-2\tinstall ok installed\n";

        var programs = InventoryCollector.ParseDpkg(output);

        Assert.Equal(["bash", "curl"], programs.Select(p => p.Name));
        Assert.Equal("5.2.21-2", programs[0].Version);
    }

    [Fact]
    public void Rpm_output_maps_vendor_and_ignores_none()
    {
        const string output = "kernel\t6.8.0-1.el9\tRed Hat, Inc.\ngpg-pubkey\t1234-5678\t(none)\n\n";

        var programs = InventoryCollector.ParseRpm(output);

        Assert.Equal(2, programs.Count);
        Assert.Equal("Red Hat, Inc.", programs.Single(p => p.Name == "kernel").Publisher);
        Assert.Null(programs.Single(p => p.Name == "gpg-pubkey").Publisher);
    }

    [Fact]
    public void Cpu_model_and_memory_are_parsed_from_proc_files()
    {
        const string cpu = "processor\t: 0\nvendor_id\t: GenuineIntel\nmodel name\t: Intel(R) Xeon(R) Gold 6230\ncpu MHz\t\t: 2100\n";
        const string memory = "MemTotal:       16384256 kB\nMemFree:         1000 kB\n";

        Assert.Equal("Intel(R) Xeon(R) Gold 6230", InventoryCollector.ParseCpuModel(cpu));
        Assert.Equal(16384256L * 1024, InventoryCollector.ParseMemTotalBytes(memory));
        Assert.Null(InventoryCollector.ParseCpuModel(null));
        Assert.Null(InventoryCollector.ParseMemTotalBytes("nothing"));
    }

    [Fact]
    public void Installed_software_without_a_name_is_dropped_and_long_values_are_cut()
    {
        var id = Guid.NewGuid();

        Assert.Null(InstalledSoftware.Create(id, "  ", "1.0", null));

        var item = InstalledSoftware.Create(id, new string('n', 1000), new string('v', 1000), "  Vendor ");
        Assert.Equal(InstalledSoftware.MaxName, item!.Name.Length);
        Assert.Equal(128, item.Version!.Length);
        Assert.Equal("Vendor", item.Publisher);
    }

    [Fact]
    public void Hardware_values_are_validated()
    {
        var hardware = DeviceHardware.Create(Guid.NewGuid());

        hardware.Apply(DateTimeOffset.UtcNow, " Dell ", "", "Cpu", -5, -1, "X64", 100, 200);

        Assert.Equal("Dell", hardware.Manufacturer);
        Assert.Null(hardware.Model);
        Assert.Null(hardware.CpuCores);
        Assert.Null(hardware.MemoryBytes);
        Assert.Equal(100, hardware.DiskTotalBytes);
    }
}
