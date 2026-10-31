using System.Text.Json;
using Odm.SharedKernel;
using Odm.SharedKernel.Contracts;

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
    public string? LastUser { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public DateTimeOffset? BootTime { get; private set; }

    /// <summary>JSON list of network adapters (name, MAC, addresses) and of volumes (name, total, free).</summary>
    public string? NetworkJson { get; private set; }
    public string? DisksJson { get; private set; }

    private const int MaxAdapters = 16;
    private const int MaxAddressesPerAdapter = 8;
    private const int MaxDisks = 32;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private DeviceHardware() { }

    public static DeviceHardware Create(Guid deviceId) => new() { DeviceId = deviceId };

    public void Apply(
        DateTimeOffset now, string? manufacturer, string? model, string? cpu, int? cpuCores, long? memoryBytes,
        string? architecture, long? diskTotalBytes, long? diskFreeBytes,
        string? lastUser = null, DateTimeOffset? lastLoginAt = null, DateTimeOffset? bootTime = null,
        IReadOnlyList<NetworkAdapterInfo>? network = null, IReadOnlyList<DiskInfo>? disks = null)
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
        LastUser = Cut(lastUser, 256);
        LastLoginAt = lastLoginAt is { } login && login > DateTimeOffset.UnixEpoch && login <= now.AddDays(1) ? login : null;
        BootTime = bootTime is { } boot && boot > DateTimeOffset.UnixEpoch && boot <= now.AddDays(1) ? boot : null;

        var adapters = (network ?? [])
            .Select(a => new NetworkAdapterInfo(
                Cut(a.Name, 128) ?? "", Cut(a.MacAddress, 32),
                a.Addresses.Select(x => Cut(x, 64)).OfType<string>().Take(MaxAddressesPerAdapter).ToList()))
            .Where(a => a.Name.Length > 0)
            .Take(MaxAdapters)
            .ToList();
        NetworkJson = adapters.Count == 0 ? null : JsonSerializer.Serialize(adapters, Json);

        var volumes = (disks ?? [])
            .Where(d => d.TotalBytes >= 0 && d.FreeBytes >= 0)
            .Select(d => new DiskInfo(Cut(d.Name, 128) ?? "", d.TotalBytes, d.FreeBytes))
            .Where(d => d.Name.Length > 0)
            .Take(MaxDisks)
            .ToList();
        DisksJson = volumes.Count == 0 ? null : JsonSerializer.Serialize(volumes, Json);
    }

    public IReadOnlyList<NetworkAdapterInfo> Adapters => Parse<NetworkAdapterInfo>(NetworkJson);

    public IReadOnlyList<DiskInfo> Volumes => Parse<DiskInfo>(DisksJson);

    private static List<T> Parse<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
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
