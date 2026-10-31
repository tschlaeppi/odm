using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Odm.Agent;

public sealed record SystemInfo(string Hostname, string OsFamily, string OsName, string OsVersion);

/// <summary>Determines host name as well as operating system name and version.</summary>
public sealed class SystemInfoProvider
{
    public static string AgentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public SystemInfo Collect()
    {
        if (OperatingSystem.IsWindows())
        {
            var (name, version) = ReadWindows();
            return new SystemInfo(Environment.MachineName, "Windows", name, version);
        }

        if (OperatingSystem.IsLinux())
        {
            var (name, version) = ReadLinux();
            return new SystemInfo(Environment.MachineName, "Linux", name, version);
        }

        throw new PlatformNotSupportedException("Only Windows and Linux are supported.");
    }

    [SupportedOSPlatform("windows")]
    private static (string Name, string Version) ReadWindows()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");

        var product = key?.GetValue("ProductName") as string ?? "Windows";
        // Windows 11 still reports "Windows 10" in the registry value; build 22000 and higher is Windows 11.
        if (int.TryParse(key?.GetValue("CurrentBuildNumber") as string, out var build) && build >= 22000)
        {
            product = product.Replace("Windows 10", "Windows 11", StringComparison.Ordinal);
        }

        var version = key?.GetValue("DisplayVersion") as string
                      ?? key?.GetValue("ReleaseId") as string
                      ?? Environment.OSVersion.Version.ToString();
        return (product, version);
    }

    private static (string Name, string Version) ReadLinux()
    {
        const string path = "/etc/os-release";
        var values = new Dictionary<string, string>();

        if (File.Exists(path))
        {
            foreach (var line in File.ReadLines(path))
            {
                var separator = line.IndexOf('=');
                if (separator <= 0 || line.StartsWith('#'))
                {
                    continue;
                }

                values[line[..separator]] = line[(separator + 1)..].Trim().Trim('"');
            }
        }

        var name = values.GetValueOrDefault("NAME", "Linux");
        var version = values.GetValueOrDefault("VERSION_ID", Environment.OSVersion.Version.ToString());
        return (name, version);
    }
}
