namespace Odm.Agent.Os;

/// <summary>Linux-Updates über apt (DEB-basiert) bzw. dnf/yum (RPM-basiert).</summary>
public sealed class LinuxUpdater : IOsUpdater
{
    private static readonly IReadOnlyDictionary<string, string> AptEnvironment = new Dictionary<string, string>
    {
        ["DEBIAN_FRONTEND"] = "noninteractive",
        ["NEEDRESTART_MODE"] = "a",
        ["LC_ALL"] = "C"
    };

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

        // Simulation: welche Pakete würden aktualisiert? ("Inst name [alt] (neu Quelle ...)")
        var simulation = await ProcessRunner.RunAsync("/usr/bin/apt-get", ["-s", "upgrade"], timeout, ct, AptEnvironment);
        var packages = simulation.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.StartsWith("Inst ", StringComparison.Ordinal))
            .Where(l => !securityOnly || l.Contains("-security", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1])
            .Distinct()
            .ToList();

        if (packages.Count == 0)
        {
            return new OsUpdateResult(true, 0, 0, [], AptRebootRequired(), null);
        }

        List<string> arguments =
        [
            "-y", "-q",
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
            return new OsUpdateResult(false, 0, packages.Count, Titles(packages), AptRebootRequired(),
                upgrade.TimedOut ? "Timed out." : $"apt-get failed (exit code {upgrade.ExitCode}). {Tail(upgrade.Error)}");
        }

        return new OsUpdateResult(true, packages.Count, 0, Titles(packages), AptRebootRequired(), null);
    }

    private static bool AptRebootRequired() =>
        File.Exists("/var/run/reboot-required") || File.Exists("/run/reboot-required");

    // ---- RPM ----

    private static async Task<OsUpdateResult> RpmAsync(string manager, bool securityOnly, TimeSpan timeout, CancellationToken ct)
    {
        string[] security = securityOnly ? ["--security"] : [];

        // Exit-Code 100 = Updates verfügbar, 0 = keine, sonst Fehler.
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
            .Select(t => t[0])
            .Distinct()
            .ToList();

        var upgrade = await ProcessRunner.RunAsync(
            manager, ["-y", .. security, Path.GetFileName(manager) == "dnf" ? "upgrade" : "update"], timeout, ct, RpmEnvironment);

        if (upgrade.TimedOut || upgrade.ExitCode != 0)
        {
            return new OsUpdateResult(false, 0, packages.Count, Titles(packages), await RpmRebootRequiredAsync(ct),
                upgrade.TimedOut ? "Timed out." : $"{Path.GetFileName(manager)} failed (exit code {upgrade.ExitCode}). {Tail(upgrade.Error)}");
        }

        return new OsUpdateResult(true, packages.Count, 0, Titles(packages), await RpmRebootRequiredAsync(ct), null);
    }

    private static async Task<bool> RpmRebootRequiredAsync(CancellationToken ct)
    {
        const string tool = "/usr/bin/needs-restarting";
        if (!File.Exists(tool))
        {
            return false; // ohne dnf-utils lässt sich das nicht zuverlässig feststellen
        }

        var result = await ProcessRunner.RunAsync(tool, ["-r"], TimeSpan.FromMinutes(2), ct);
        return result.ExitCode == 1; // 1 = Neustart erforderlich
    }

    private static IReadOnlyList<string> Titles(List<string> packages) => packages.Take(50).ToList();

    private static OsUpdateResult Fail(string message) => new(false, 0, 0, [], false, message);

    private static string Tail(string text) => text.Length <= 500 ? text.Trim() : text[^500..].Trim();
}
