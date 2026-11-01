namespace Odm.Agent.Os;

/// <summary>Linux updates through apt (DEB based) or dnf/yum (RPM based).</summary>
public sealed class LinuxUpdater : IOsUpdater
{
    private static readonly IReadOnlyDictionary<string, string> AptEnvironment = new Dictionary<string, string>
    {
        ["DEBIAN_FRONTEND"] = "noninteractive",
        ["NEEDRESTART_MODE"] = "a",
        ["LC_ALL"] = "C"
    };

    // Ubuntu phased updates would otherwise be held back by "apt-get upgrade" although "apt list --upgradable" shows them.
    // Staged rollouts are controlled by ODM itself, so all available updates are included.
    private const string IncludePhased = "APT::Get::Always-Include-Phased-Updates=true";

    private static readonly IReadOnlyDictionary<string, string> RpmEnvironment = new Dictionary<string, string>
    {
        ["LC_ALL"] = "C"
    };

    public async Task<OsUpdateResult> RunAsync(OsUpdatePayload payload, string workDirectory, CancellationToken ct)
    {
        var timeout = TimeSpan.FromMinutes(Math.Clamp(payload.TimeoutMinutes, 5, 24 * 60));

        if (File.Exists("/usr/bin/apt-get"))
        {
            return await AptAsync(payload.Mode == "SecurityOnly", timeout, ct);
        }

        var rpm = new[] { "/usr/bin/dnf", "/usr/bin/yum" }.FirstOrDefault(File.Exists);
        if (rpm is not null)
        {
            return await RpmAsync(rpm, payload.Mode == "SecurityOnly", timeout, ct);
        }

        return new OsUpdateResult(false, 0, 0, [], false, "No supported package manager found (apt, dnf, yum).");
    }

    // ---- DEB ----

    private static async Task<OsUpdateResult> AptAsync(bool securityOnly, TimeSpan timeout, CancellationToken ct)
    {
        var update = await ProcessRunner.RunAsync("/usr/bin/apt-get", ["update", "-q"], timeout, ct, AptEnvironment);
        if (update.ExitCode != 0)
        {
            return Fail($"apt-get update failed (exit code {update.ExitCode}). {Tail(update.Error)}");
        }

        // Simulation: which packages would be upgraded? ("Inst name [old] (new source ...)")
        var simulation = await ProcessRunner.RunAsync("/usr/bin/apt-get", ["-s", "-o", IncludePhased, "upgrade"], timeout, ct, AptEnvironment);
        var lines = simulation.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.StartsWith("Inst ", StringComparison.Ordinal))
            .Where(l => !securityOnly || l.Contains("-security", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var packages = lines
            .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1])
            .Distinct()
            .ToList();
        var titles = lines.Select(AptTitle).Distinct().ToList();

        if (packages.Count == 0)
        {
            return new OsUpdateResult(true, 0, 0, [], AptRebootRequired(), "No updates available.");
        }

        List<string> arguments =
        [
            "-y", "-q",
            "-o", IncludePhased,
            "-o", "Dpkg::Options::=--force-confdef",
            "-o", "Dpkg::Options::=--force-confold"
        ];

        if (securityOnly)
        {
            arguments.AddRange(["install", "--only-upgrade", .. packages]);
        }
        else
        {
            arguments.Add("upgrade");
        }

        var upgrade = await ProcessRunner.RunAsync("/usr/bin/apt-get", arguments, timeout, ct, AptEnvironment);
        if (upgrade.TimedOut || upgrade.ExitCode != 0)
        {
            return new OsUpdateResult(false, 0, packages.Count, Titles(titles), AptRebootRequired(),
                upgrade.TimedOut ? "Timed out." : $"apt-get failed (exit code {upgrade.ExitCode}). {Tail(upgrade.Error)}");
        }

        return new OsUpdateResult(true, packages.Count, 0, Titles(titles), AptRebootRequired(), null);
    }

    private static bool AptRebootRequired() =>
        File.Exists("/var/run/reboot-required") || File.Exists("/run/reboot-required");

    // ---- RPM ----

    private static async Task<OsUpdateResult> RpmAsync(string manager, bool securityOnly, TimeSpan timeout, CancellationToken ct)
    {
        string[] security = securityOnly ? ["--security"] : [];

        // Exit code 100 = updates available, 0 = none, otherwise error.
        var check = await ProcessRunner.RunAsync(manager, ["-q", "check-update", .. security], timeout, ct, RpmEnvironment);
        if (check.ExitCode == 0)
        {
            return new OsUpdateResult(true, 0, 0, [], await RpmRebootRequiredAsync(ct), null);
        }

        if (check.ExitCode != 100)
        {
            return Fail($"{Path.GetFileName(manager)} check-update failed (exit code {check.ExitCode}). {Tail(check.Error)}");
        }

        var packages = check.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(t => t.Length >= 3 && t[0].Contains('.') && t[1].Any(char.IsDigit))
            .Select(t => (Name: t[0], Title: $"{t[0]} {t[1]}"))
            .DistinctBy(t => t.Name)
            .ToList();

        var upgrade = await ProcessRunner.RunAsync(
            manager, ["-y", .. security, Path.GetFileName(manager) == "dnf" ? "upgrade" : "update"], timeout, ct, RpmEnvironment);

        if (upgrade.TimedOut || upgrade.ExitCode != 0)
        {
            return new OsUpdateResult(false, 0, packages.Count, Titles(packages.Select(p => p.Title).ToList()), await RpmRebootRequiredAsync(ct),
                upgrade.TimedOut ? "Timed out." : $"{Path.GetFileName(manager)} failed (exit code {upgrade.ExitCode}). {Tail(upgrade.Error)}");
        }

        return new OsUpdateResult(true, packages.Count, 0, Titles(packages.Select(p => p.Title).ToList()), await RpmRebootRequiredAsync(ct), null);
    }

    private static async Task<bool> RpmRebootRequiredAsync(CancellationToken ct)
    {
        const string tool = "/usr/bin/needs-restarting";
        if (!File.Exists(tool))
        {
            return false; // without dnf-utils this cannot be determined reliably
        }

        var result = await ProcessRunner.RunAsync(tool, ["-r"], TimeSpan.FromMinutes(2), ct);
        return result.ExitCode == 1; // 1 = restart required
    }

    /// <summary>"Inst name [old] (new source ...)" becomes "name old → new".</summary>
    public static string AptTitle(string installLine)
    {
        var parts = installLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var name = parts[1];
        var old = parts.Length > 2 && parts[2].StartsWith('[') ? parts[2].Trim('[', ']') : null;
        var next = parts.FirstOrDefault(p => p.StartsWith('('))?.TrimStart('(');
        return (old, next) switch
        {
            (not null, not null) => $"{name} {old} → {next}",
            (null, not null) => $"{name} {next}",
            _ => name
        };
    }

    // Upper bound so the job output stays small (the server keeps at most 1000 per run anyway).
    private static IReadOnlyList<string> Titles(List<string> packages) => packages.Take(500).ToList();

    private static OsUpdateResult Fail(string message) => new(false, 0, 0, [], false, message);

    private static string Tail(string text) => text.Length <= 500 ? text.Trim() : text[^500..].Trim();
}
