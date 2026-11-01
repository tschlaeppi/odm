using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Odm.Agent.Os;
using Odm.Agent.Repo;

namespace Odm.Agent.Inventory;

// ---- Protocol (/api/agent/v1/inventory), counterpart of the server contracts ----

public sealed record AdapterReport(string Name, string? MacAddress, IReadOnlyList<string> Addresses);

public sealed record DiskReport(string Name, long TotalBytes, long FreeBytes);

public sealed record HardwareReport(
    string? Manufacturer, string? Model, string? Cpu, int? CpuCores, long? MemoryBytes,
    string? Architecture, long? DiskTotalBytes, long? DiskFreeBytes,
    string? LastUser = null, DateTimeOffset? LastLoginAt = null, DateTimeOffset? BootTime = null,
    IReadOnlyList<AdapterReport>? Network = null, IReadOnlyList<DiskReport>? Disks = null);

public sealed record ProgramReport(string Name, string? Version, string? Publisher);

public sealed record UpdateReport(string Title, DateTimeOffset InstalledAt);

/// <summary>Updates = history of the operating system (null if it could not be read; the server then keeps its data).</summary>
public sealed record InventoryRequest(
    HardwareReport Hardware, IReadOnlyList<ProgramReport> Software, IReadOnlyList<UpdateReport>? Updates = null);

/// <summary>
/// Collects basic hardware data and the installed programs: Windows from the machine-wide uninstall registry keys
/// (per-user installs are not included), Linux from dpkg or rpm. Collection problems never stop the agent: what cannot be
/// read is left empty.
/// </summary>
public sealed partial class InventoryCollector(ILogger<InventoryCollector> logger)
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(60);

    public async Task<InventoryRequest> CollectAsync(CancellationToken ct)
    {
        var hardware = CollectHardware() with
        {
            Network = CollectNetwork(),
            Disks = CollectVolumes(),
            BootTime = ReadBootTime()
        };
        var (lastUser, lastLogin) = await CollectLastLoginAsync(ct);
        hardware = hardware with { LastUser = lastUser, LastLoginAt = lastLogin };
        var software = await CollectSoftwareAsync(ct);
        var updates = await CollectUpdatesAsync(ct);
        return new InventoryRequest(hardware, software, updates);
    }

    // ---- Hardware ----

    private HardwareReport CollectHardware()
    {
        string? manufacturer = null, model = null, cpu = null;
        long? memory = null;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                manufacturer = ReadRegistry(@"HARDWARE\DESCRIPTION\System\BIOS", "SystemManufacturer");
                model = ReadRegistry(@"HARDWARE\DESCRIPTION\System\BIOS", "SystemProductName");
                cpu = ReadRegistry(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString")?.Trim();
            }
            else if (OperatingSystem.IsLinux())
            {
                manufacturer = ReadFile("/sys/class/dmi/id/sys_vendor");
                model = ReadFile("/sys/class/dmi/id/product_name");
                cpu = ParseCpuModel(ReadFile("/proc/cpuinfo", 200_000));
                memory = ParseMemTotalBytes(ReadFile("/proc/meminfo", 20_000));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Hardware data could not be read completely.");
        }

        memory ??= GC.GetGCMemoryInfo().TotalAvailableMemoryBytes is > 0 and var total ? total : null;
        var (diskTotal, diskFree) = ReadDisks();

        return new HardwareReport(
            Clean(manufacturer), Clean(model), Clean(cpu), Environment.ProcessorCount, memory,
            RuntimeInformation.OSArchitecture.ToString(), diskTotal, diskFree);
    }

    // ---- Network, volumes, uptime, last login ----

    private static readonly string[] VirtualAdapterPrefixes = ["veth", "docker", "br-", "virbr", "cni", "flannel", "cali", "lo"];

    private IReadOnlyList<AdapterReport> CollectNetwork()
    {
        try
        {
            var result = new List<AdapterReport>();
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up ||
                    adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel ||
                    VirtualAdapterPrefixes.Any(p => adapter.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var addresses = adapter.GetIPProperties().UnicastAddresses
                    .Select(a => a.Address)
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork ||
                                (a.AddressFamily == AddressFamily.InterNetworkV6 && !a.IsIPv6LinkLocal && !a.IsIPv6Multicast))
                    .Select(a => a.ToString())
                    .ToList();
                if (addresses.Count == 0)
                {
                    continue;
                }

                var mac = adapter.GetPhysicalAddress().ToString();
                result.Add(new AdapterReport(
                    adapter.Name, FormatMac(mac), addresses.OrderBy(a => a.Contains(':')).ToList()));
            }

            return result;
        }
        catch (Exception ex) when (ex is NetworkInformationException or UnauthorizedAccessException or IOException)
        {
            logger.LogDebug(ex, "Network adapters could not be read.");
            return [];
        }
    }

    public static string? FormatMac(string? raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.Length != 12 || raw.All(c => c == '0'))
        {
            return null;
        }

        return string.Join(':', Enumerable.Range(0, 6).Select(i => raw.Substring(i * 2, 2)));
    }

    private static readonly string[] LinuxFileSystems = ["ext2", "ext3", "ext4", "xfs", "btrfs", "zfs", "f2fs", "jfs", "ntfs", "exfat", "vfat"];

    private IReadOnlyList<DiskReport> CollectVolumes()
    {
        try
        {
            var drives = OperatingSystem.IsWindows()
                ? DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                : DriveInfo.GetDrives().Where(d =>
                    d.IsReady && LinuxFileSystems.Contains(d.DriveFormat, StringComparer.OrdinalIgnoreCase) &&
                    !d.Name.StartsWith("/snap", StringComparison.Ordinal) && !d.Name.StartsWith("/var/lib/", StringComparison.Ordinal));
            return drives
                .Where(d => d.TotalSize > 0)
                .DistinctBy(d => d.Name)
                .Select(d => new DiskReport(d.Name, d.TotalSize, d.AvailableFreeSpace))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Time of the last start; the server shows the uptime derived from it.</summary>
    private static DateTimeOffset? ReadBootTime()
    {
        try
        {
            TimeSpan? uptime = null;
            if (OperatingSystem.IsLinux())
            {
                uptime = ParseUptime(ReadFile("/proc/uptime", 200));
            }
            else if (Environment.TickCount64 > 0)
            {
                uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            }

            return uptime is { } up ? DateTimeOffset.UtcNow - up : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Content of /proc/uptime: seconds since the start and idle time.</summary>
    public static TimeSpan? ParseUptime(string? text)
    {
        var first = text?.Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    private async Task<(string? User, DateTimeOffset? At)> CollectLastLoginAsync(CancellationToken ct)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return ReadWindowsLastLogin();
            }

            if (OperatingSystem.IsLinux() && ToolLocator.Linux("last") is { } last)
            {
                var result = await ProcessRunner.RunAsync(
                    last, ["-w", "-F", "--time-format", "iso", "-n", "50"], CommandTimeout, ct,
                    new Dictionary<string, string> { ["LC_ALL"] = "C" });
                return result.ExitCode == 0 ? ParseLast(result.Output) : (null, null);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "The last login could not be determined.");
        }

        return (null, null);
    }

    private static readonly string[] NotUsers = ["reboot", "shutdown", "runlevel", "wtmp", "btmp"];

    /// <summary>Output of <c>last -w -F --time-format iso</c>: first line with a real user and an ISO timestamp.</summary>
    public static (string? User, DateTimeOffset? At) ParseLast(string output)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2 || NotUsers.Contains(tokens[0], StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var token in tokens.Skip(1))
            {
                if (token.Length >= 19 && char.IsDigit(token[0]) && token[4] == '-' && token[10] == 'T' &&
                    DateTimeOffset.TryParse(token, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
                {
                    return (tokens[0], at.ToUniversalTime());
                }
            }
        }

        return (null, null);
    }

    [SupportedOSPlatform("windows")]
    private static (string? User, DateTimeOffset? At) ReadWindowsLastLogin()
    {
        const string logonUi = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI";
        var sam = ReadRegistry(logonUi, "LastLoggedOnSAMUser") ?? ReadRegistry(logonUi, "LastLoggedOnUser");

        var profiles = new List<(string Leaf, DateTimeOffset At)>();
        using var list = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
        foreach (var sid in list?.GetSubKeyNames() ?? [])
        {
            if (!sid.StartsWith("S-1-5-21-", StringComparison.Ordinal) && !sid.StartsWith("S-1-12-1-", StringComparison.Ordinal))
            {
                continue; // local users, domain users and Entra ID users; no service accounts
            }

            using var entry = list!.OpenSubKey(sid);
            var path = entry?.GetValue("ProfileImagePath")?.ToString();
            if (path is null || entry!.GetValue("LocalProfileLoadTimeHigh") is not int high ||
                entry.GetValue("LocalProfileLoadTimeLow") is not int low)
            {
                continue;
            }

            var fileTime = ((long)(uint)high << 32) | (uint)low;
            if (fileTime > 0)
            {
                profiles.Add((Path.GetFileName(path.TrimEnd('\\')), new DateTimeOffset(DateTime.FromFileTimeUtc(fileTime), TimeSpan.Zero)));
            }
        }

        return PickLastLogin(sam, profiles);
    }

    /// <summary>
    /// The name comes from the last user shown on the sign-in screen; the time is the last load of that user's profile
    /// (profile folders of domain users may carry a suffix such as <c>name.DOMAIN</c>). Without a name the newest profile counts.
    /// </summary>
    public static (string? User, DateTimeOffset? At) PickLastLogin(string? samUser, IReadOnlyCollection<(string Leaf, DateTimeOffset At)> profiles)
    {
        var name = string.IsNullOrWhiteSpace(samUser) ? null : samUser.Trim();
        var shortName = name is null ? null : name[(name.LastIndexOf('\\') + 1)..];

        if (shortName is not null)
        {
            var matches = profiles
                .Where(p => p.Leaf.Equals(shortName, StringComparison.OrdinalIgnoreCase) ||
                            p.Leaf.StartsWith(shortName + ".", StringComparison.OrdinalIgnoreCase))
                .ToList();
            return (name, matches.Count == 0 ? null : matches.Max(p => p.At));
        }

        if (profiles.Count == 0)
        {
            return (null, null);
        }

        var newest = profiles.MaxBy(p => p.At);
        return (newest.Leaf, newest.At);
    }

    private static (long? Total, long? Free) ReadDisks()
    {
        try
        {
            var drives = OperatingSystem.IsWindows()
                ? DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).ToList()
                : DriveInfo.GetDrives().Where(d => d.IsReady && d.Name == "/").ToList();
            return drives.Count == 0 ? (null, null) : (drives.Sum(d => d.TotalSize), drives.Sum(d => d.AvailableFreeSpace));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, null);
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadRegistry(string subKey, string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(subKey);
        return key?.GetValue(name)?.ToString();
    }

    private static string? ReadFile(string path, int maxChars = 1000)
    {
        try
        {
            using var reader = new StreamReader(path);
            var buffer = new char[maxChars];
            var read = reader.ReadBlock(buffer, 0, maxChars);
            return new string(buffer, 0, read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string? ParseCpuModel(string? cpuInfo)
    {
        if (cpuInfo is null)
        {
            return null;
        }

        foreach (var line in cpuInfo.Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator > 0 && line[..separator].Trim() is "model name" or "Model" or "Hardware" or "cpu model")
            {
                return line[(separator + 1)..].Trim();
            }
        }

        return null;
    }

    public static long? ParseMemTotalBytes(string? memInfo)
    {
        var match = MemTotal().Match(memInfo ?? "");
        return match.Success && long.TryParse(match.Groups[1].Value, out var kb) ? kb * 1024 : null;
    }

    [GeneratedRegex(@"^MemTotal:\s+(\d+)\s*kB", RegexOptions.Multiline)]
    private static partial Regex MemTotal();

    private static string? Clean(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    // ---- Update history of the operating system ----

    private static readonly TimeSpan HistoryWindow = TimeSpan.FromDays(400);

    private async Task<IReadOnlyList<UpdateReport>?> CollectUpdatesAsync(CancellationToken ct)
    {
        try
        {
            var since = DateTimeOffset.UtcNow - HistoryWindow;
            if (OperatingSystem.IsWindows())
            {
                return await Task.Run(() => OperatingSystem.IsWindows() ? WindowsUpdater.ReadHistory(since) : null, ct);
            }

            if (OperatingSystem.IsLinux() && File.Exists("/var/log/dpkg.log"))
            {
                return ReadDpkgHistory("/var/log", since);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The update history of the operating system could not be read.");
        }

        return null;
    }

    private static List<UpdateReport> ReadDpkgHistory(string directory, DateTimeOffset since)
    {
        var architecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "armhf",
            Architecture.X86 => "i386",
            _ => "amd64"
        };
        var updates = new List<UpdateReport>();
        foreach (var file in Directory.EnumerateFiles(directory, "dpkg.log*").OrderBy(f => f, StringComparer.Ordinal))
        {
            try
            {
                using var raw = File.OpenRead(file);
                using Stream stream = file.EndsWith(".gz", StringComparison.Ordinal)
                    ? new System.IO.Compression.GZipStream(raw, System.IO.Compression.CompressionMode.Decompress)
                    : raw;
                using var reader = new StreamReader(stream);
                while (reader.ReadLine() is { } line)
                {
                    if (ParseDpkgLine(line, architecture) is { } update && update.InstalledAt >= since)
                    {
                        updates.Add(update);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // unreadable log file: skip it
            }
        }

        return updates;
    }

    /// <summary>
    /// "2024-05-01 06:12:33 upgrade libssl3:amd64 3.0.2-1 3.0.2-2" becomes "libssl3 3.0.2-1 → 3.0.2-2" (the same
    /// format the update runs use). Only upgrades count as updates; other actions are ignored.
    /// </summary>
    public static UpdateReport? ParseDpkgLine(string line, string nativeArchitecture)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 6 || parts[2] != "upgrade")
        {
            return null;
        }

        if (!DateTime.TryParseExact(
                $"{parts[0]} {parts[1]}", "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var local))
        {
            return null;
        }

        var name = parts[3];
        var colon = name.IndexOf(':', StringComparison.Ordinal);
        if (colon > 0 && name[(colon + 1)..] is var arch && (arch == nativeArchitecture || arch == "all"))
        {
            name = name[..colon];
        }

        return new UpdateReport($"{name} {parts[4]} → {parts[5]}", new DateTimeOffset(local));
    }

    // ---- Software ----

    private async Task<IReadOnlyList<ProgramReport>> CollectSoftwareAsync(CancellationToken ct)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return CollectWindowsPrograms();
            }

            if (OperatingSystem.IsLinux())
            {
                if (ToolLocator.Linux("dpkg-query") is { } dpkg)
                {
                    var result = await ProcessRunner.RunAsync(
                        dpkg, ["-W", "-f=${Package}\t${Version}\t${Status}\n"], CommandTimeout, ct);
                    return ParseDpkg(result.Output);
                }

                if (ToolLocator.Linux("rpm") is { } rpm)
                {
                    var result = await ProcessRunner.RunAsync(
                        rpm, ["-qa", "--qf", "%{NAME}\t%{VERSION}-%{RELEASE}\t%{VENDOR}\n"], CommandTimeout, ct);
                    return ParseRpm(result.Output);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The installed programs could not be listed.");
        }

        return [];
    }

    [SupportedOSPlatform("windows")]
    private static List<ProgramReport> CollectWindowsPrograms()
    {
        var programs = new List<ProgramReport>();
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null)
            {
                continue;
            }

            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var entry = uninstall.OpenSubKey(name);
                var displayName = entry?.GetValue("DisplayName")?.ToString();
                if (string.IsNullOrWhiteSpace(displayName) ||
                    entry!.GetValue("SystemComponent") is 1 ||
                    !string.IsNullOrEmpty(entry.GetValue("ParentKeyName")?.ToString()))
                {
                    continue; // hidden components and updates of other programs
                }

                programs.Add(new ProgramReport(
                    displayName.Trim(), entry.GetValue("DisplayVersion")?.ToString()?.Trim(), entry.GetValue("Publisher")?.ToString()?.Trim()));
            }
        }

        return Distinct(programs);
    }

    /// <summary>Lines of <c>name TAB version TAB status</c>; only fully installed packages count.</summary>
    public static IReadOnlyList<ProgramReport> ParseDpkg(string output)
    {
        var programs = new List<ProgramReport>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.TrimEnd('\r').Split('\t');
            if (parts.Length >= 3 && parts[0].Length > 0 && parts[2].EndsWith(" installed", StringComparison.Ordinal))
            {
                programs.Add(new ProgramReport(parts[0], parts[1], null));
            }
        }

        return Distinct(programs);
    }

    /// <summary>Lines of <c>name TAB version-release TAB vendor</c>.</summary>
    public static IReadOnlyList<ProgramReport> ParseRpm(string output)
    {
        var programs = new List<ProgramReport>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.TrimEnd('\r').Split('\t');
            if (parts.Length >= 2 && parts[0].Length > 0)
            {
                var vendor = parts.Length > 2 && parts[2] != "(none)" ? parts[2] : null;
                programs.Add(new ProgramReport(parts[0], parts[1], vendor));
            }
        }

        return Distinct(programs);
    }

    private static List<ProgramReport> Distinct(IEnumerable<ProgramReport> programs) =>
        programs
            .DistinctBy(p => (p.Name.ToLowerInvariant(), p.Version))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
