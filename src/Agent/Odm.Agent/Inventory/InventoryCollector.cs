using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Odm.Agent.Os;
using Odm.Agent.Repo;

namespace Odm.Agent.Inventory;

// ---- Protocol (/api/agent/v1/inventory), counterpart of the server contracts ----

public sealed record HardwareReport(
    string? Manufacturer, string? Model, string? Cpu, int? CpuCores, long? MemoryBytes,
    string? Architecture, long? DiskTotalBytes, long? DiskFreeBytes);

public sealed record ProgramReport(string Name, string? Version, string? Publisher);

public sealed record InventoryRequest(HardwareReport Hardware, IReadOnlyList<ProgramReport> Software);

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
        var hardware = CollectHardware();
        var software = await CollectSoftwareAsync(ct);
        return new InventoryRequest(hardware, software);
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
