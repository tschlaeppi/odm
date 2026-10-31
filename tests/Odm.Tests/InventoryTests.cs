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

    [Fact]
    public void Last_output_gives_first_real_user_with_time()
    {
        const string output =
            "reboot   system boot  6.8.0 2026-10-31T13:12:00+01:00   still running\n" +
            "localadmin tty2 tty2 2026-10-31T09:10:00+01:00 still logged in\n" +
            "anna     pts/0 10.0.0.5 2026-10-30T08:00:00+01:00 - 2026-10-30T09:00:00+01:00  (01:00)\n";

        var (user, at) = InventoryCollector.ParseLast(output);

        Assert.Equal("localadmin", user);
        Assert.Equal(DateTimeOffset.Parse("2026-10-31T08:10:00Z"), at);
        Assert.Equal((null, null), InventoryCollector.ParseLast("wtmp begins Fri Oct 31 2026\n"));
    }

    [Fact]
    public void Windows_last_login_takes_name_from_sign_in_screen_and_time_from_profile()
    {
        var old = DateTimeOffset.Parse("2026-10-01T10:00:00Z");
        var recent = DateTimeOffset.Parse("2026-10-30T10:00:00Z");
        var profiles = new[] { ("anna.CORP", old), ("tom", recent), ("anna", recent.AddDays(-5)) };

        var (user, at) = InventoryCollector.PickLastLogin(@"CORP\anna", profiles);

        Assert.Equal(@"CORP\anna", user);
        Assert.Equal(recent.AddDays(-5), at);
        Assert.Equal(("tom", recent), InventoryCollector.PickLastLogin(null, profiles));
        Assert.Equal((null, null), InventoryCollector.PickLastLogin(null, []));
    }

    [Fact]
    public void Uptime_and_mac_parsing()
    {
        Assert.Equal(TimeSpan.FromSeconds(3600.5), InventoryCollector.ParseUptime("3600.50 12000.00\n"));
        Assert.Null(InventoryCollector.ParseUptime("x"));
        Assert.Equal("00:11:22:AA:BB:CC", InventoryCollector.FormatMac("001122AABBCC"));
        Assert.Null(InventoryCollector.FormatMac("000000000000"));
    }

    [Fact]
    public void Hardware_keeps_network_disks_and_login_and_drops_implausible_values()
    {
        var hardware = DeviceHardware.Create(Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;

        hardware.Apply(
            now, "Dell", "XPS", "cpu", 8, 1, "X64", 100, 50, "  anna ", now.AddDays(-1), now.AddDays(30),
            [new Odm.SharedKernel.Contracts.NetworkAdapterInfo("eth0", "00:11:22:33:44:55", ["10.0.0.5", "fe80::1"])],
            [new Odm.SharedKernel.Contracts.DiskInfo("/", 1000, 400), new Odm.SharedKernel.Contracts.DiskInfo("bad", -1, 0)]);

        Assert.Equal("anna", hardware.LastUser);
        Assert.NotNull(hardware.LastLoginAt);
        Assert.Null(hardware.BootTime); // in the future
        Assert.Equal("10.0.0.5", Assert.Single(hardware.Adapters).Addresses[0]);
        Assert.Equal("/", Assert.Single(hardware.Volumes).Name);
    }
}
