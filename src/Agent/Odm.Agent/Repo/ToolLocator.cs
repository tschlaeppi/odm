namespace Odm.Agent.Repo;

/// <summary>Findet Paketmanager-Programme, auch wenn der Dienst keinen vollständigen PATH hat.</summary>
public static class ToolLocator
{
    private static readonly string[] LinuxDirectories = ["/usr/bin", "/bin", "/usr/sbin", "/usr/local/bin", "/snap/bin"];

    public static string? Linux(string name) =>
        LinuxDirectories.Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);

    public static string? Chocolatey()
    {
        var root = Environment.GetEnvironmentVariable("ChocolateyInstall") ?? @"C:\ProgramData\chocolatey";
        var path = Path.Combine(root, "bin", "choco.exe");
        return File.Exists(path) ? path : OnPath("choco.exe");
    }

    /// <summary>
    /// Winget gehört zum "App-Installer" (MSIX) und liegt unter WindowsApps. Als SYSTEM ist es nicht im PATH,
    /// deshalb wird die neueste installierte Version direkt gesucht.
    /// </summary>
    public static string? Winget()
    {
        try
        {
            var apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            if (Directory.Exists(apps))
            {
                var best = Directory.GetDirectories(apps, "Microsoft.DesktopAppInstaller_*_8wekyb3d8bbwe")
                    .Select(d => (Directory: d, Version: ParseVersion(Path.GetFileName(d))))
                    .Where(x => x.Version is not null && (x.Directory.Contains("_x64_") || x.Directory.Contains("_arm64_")))
                    .OrderByDescending(x => x.Version)
                    .Select(x => Path.Combine(x.Directory, "winget.exe"))
                    .FirstOrDefault(File.Exists);
                if (best is not null)
                {
                    return best;
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // kein Zugriff: auf PATH ausweichen
        }

        return OnPath("winget.exe");
    }

    // "Microsoft.DesktopAppInstaller_1.24.2661.0_x64__8wekyb3d8bbwe" -> 1.24.2661.0
    private static Version? ParseVersion(string directoryName)
    {
        var parts = directoryName.Split('_');
        return parts.Length > 1 && Version.TryParse(parts[1], out var version) ? version : null;
    }

    private static string? OnPath(string fileName) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => Path.Combine(d, fileName))
            .FirstOrDefault(File.Exists);
}
