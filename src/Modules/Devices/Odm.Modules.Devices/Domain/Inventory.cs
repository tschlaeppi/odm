using Odm.SharedKernel;

namespace Odm.Modules.Devices.Domain;

/// <summary>Hardware snapshot of a device as last reported by its agent (one row per device).</summary>
public sealed class DeviceHardware
{
    public Guid DeviceId { get; private set; }
    public DateTimeOffset CollectedAt { get; private set; }
    public string? Manufacturer { get; private set; }
    public string? Model { get; private set; }
    public string? Cpu { get; private set; }
    public int? CpuCores { get; private set; }
    public long? MemoryBytes { get; private set; }
    public string? Architecture { get; private set; }
    public long? DiskTotalBytes { get; private set; }
    public long? DiskFreeBytes { get; private set; }

    private DeviceHardware() { }

    public static DeviceHardware Create(Guid deviceId) => new() { DeviceId = deviceId };

    public void Apply(
        DateTimeOffset now, string? manufacturer, string? model, string? cpu, int? cpuCores, long? memoryBytes,
        string? architecture, long? diskTotalBytes, long? diskFreeBytes)
    {
        CollectedAt = now;
        Manufacturer = Cut(manufacturer, 128);
        Model = Cut(model, 128);
        Cpu = Cut(cpu, 256);
        CpuCores = cpuCores is > 0 and < 100_000 ? cpuCores : null;
        MemoryBytes = memoryBytes is > 0 ? memoryBytes : null;
        Architecture = Cut(architecture, 32);
        DiskTotalBytes = diskTotalBytes is >= 0 ? diskTotalBytes : null;
        DiskFreeBytes = diskFreeBytes is >= 0 ? diskFreeBytes : null;
    }

    internal static string? Cut(string? value, int max)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length <= max ? text : text[..max];
    }
}

/// <summary>A program installed on a device as reported by its agent (not only packages distributed by ODM).</summary>
public sealed class InstalledSoftware : Entity
{
    public const int MaxName = 256;

    public Guid DeviceId { get; private set; }
    public string Name { get; private set; } = "";
    public string? Version { get; private set; }
    public string? Publisher { get; private set; }

    private InstalledSoftware() { }

    /// <summary>Returns null for entries without a usable name.</summary>
    public static InstalledSoftware? Create(Guid deviceId, string? name, string? version, string? publisher)
    {
        var cleanName = DeviceHardware.Cut(name, MaxName);
        return cleanName is null
            ? null
            : new InstalledSoftware
            {
                DeviceId = deviceId,
                Name = cleanName,
                Version = DeviceHardware.Cut(version, 128),
                Publisher = DeviceHardware.Cut(publisher, 256)
            };
    }
}
