using Odm.Agent.Os;

namespace Odm.Agent.Repo;

/// <summary>Inhalt des Auftrags "install-repo" (Gegenstück zum Server).</summary>
public sealed record InstallRepoPayload(
    string Manager, string Identifier, string? Version, string? Scope, string? Source, string? Remote,
    string? Channel, bool Classic, string? Arguments, int TimeoutMinutes, bool Uninstall = false);

public sealed record RepoResult(bool Success, string Output);

/// <summary>
/// Installiert oder aktualisiert ein Paket über den Paketmanager des Geräts. Alle Aufrufe sind
/// nicht interaktiv ("unattended"). Ohne feste Version wird auf die neueste Version gebracht.
/// </summary>
public static class RepoInstaller
{
    // Winget: 0x8A15002B = kein anwendbares Update (schon aktuell), 0x8A150061 = bereits installiert.
    private static readonly HashSet<int> WingetOk = [0, unchecked((int)0x8A15002B), unchecked((int)0x8A150061), 1641, 3010];
    private static readonly HashSet<int> ChocolateyOk = [0, 1641, 3010];

    private static readonly IReadOnlyDictionary<string, string> AptEnvironment = new Dictionary<string, string>
    {
        ["DEBIAN_FRONTEND"] = "noninteractive",
        ["NEEDRESTART_MODE"] = "a",
        ["LC_ALL"] = "C"
    };

    // Winget: 0x8A150014 = kein installiertes Paket gefunden (schon entfernt).
    private static readonly HashSet<int> WingetRemoveOk = [0, unchecked((int)0x8A150014), 1641, 3010];

    public static Task<RepoResult> RunAsync(
        InstallRepoPayload p, IReadOnlyList<string> extra, TimeSpan timeout, CancellationToken ct) =>
        p.Uninstall ? UninstallAsync(p, timeout, ct) : InstallOrUpdateAsync(p, extra, timeout, ct);

    private static Task<RepoResult> InstallOrUpdateAsync(
        InstallRepoPayload p, IReadOnlyList<string> extra, TimeSpan timeout, CancellationToken ct) => p.Manager switch
    {
        "Winget" => WingetAsync(p, extra, timeout, ct),
        "Chocolatey" => ChocolateyAsync(p, extra, timeout, ct),
        "Apt" => AptAsync(p, extra, timeout, ct),
        "Rpm" => RpmAsync(p, extra, timeout, ct),
        "Flatpak" => FlatpakAsync(p, extra, timeout, ct),
        "Snap" => SnapAsync(p, extra, timeout, ct),
        _ => Task.FromResult(Fail($"Unbekannter Paketmanager '{p.Manager}'."))
    };

    // ---- Argumente (rein, ohne Prozessstart) ----

    public static List<string> WingetArguments(InstallRepoPayload p, IReadOnlyList<string> extra)
    {
        List<string> args =
        [
            "install", "--id", p.Identifier, "--exact",
            "--silent", "--disable-interactivity",
            "--accept-package-agreements", "--accept-source-agreements",
            "--scope", p.Scope == "user" ? "user" : "machine"
        ];
        if (p.Source is not null) args.AddRange(["--source", p.Source]);
        if (p.Version is not null) args.AddRange(["--version", p.Version]);
        args.AddRange(extra);
        return args;
    }

    public static List<string> ChocolateyArguments(InstallRepoPayload p, IReadOnlyList<string> extra)
    {
        // "upgrade" installiert fehlende Pakete und aktualisiert vorhandene
        List<string> args = ["upgrade", p.Identifier, "-y", "--no-progress"];
        if (p.Version is not null) args.AddRange(["--version", p.Version, "--allow-downgrade"]);
        if (p.Source is not null) args.AddRange(["--source", p.Source]);
        args.AddRange(extra);
        return args;
    }

    public static List<string> AptArguments(InstallRepoPayload p, IReadOnlyList<string> extra)
    {
        List<string> args = ["install", "-y", "-q"];
        if (p.Version is not null) args.Add("--allow-downgrades");
        args.AddRange(extra);
        args.Add(p.Version is null ? p.Identifier : $"{p.Identifier}={p.Version}");
        return args;
    }

    public static List<string> RpmArguments(string manager, bool installed, InstallRepoPayload p, IReadOnlyList<string> extra)
    {
        // Feste Version: immer "install name-version". Sonst: vorhandenes Paket aktualisieren, fehlendes installieren.
        var verb = p.Version is null && installed ? (manager == "yum" ? "update" : "upgrade") : "install";
        List<string> args = ["-y", verb];
        args.AddRange(extra);
        args.Add(p.Version is null ? p.Identifier : $"{p.Identifier}-{p.Version}");
        return args;
    }

    public static List<string> FlatpakInstallArguments(InstallRepoPayload p, IReadOnlyList<string> extra)
    {
        List<string> args = ["install", "-y", "--noninteractive", ScopeFlag(p), p.Remote ?? "flathub", p.Identifier];
        args.AddRange(extra);
        return args;
    }

    public static List<string> FlatpakUpdateArguments(InstallRepoPayload p, IReadOnlyList<string> extra)
    {
        // Bei Flatpak entspricht eine "Version" einer Commit-ID.
        List<string> args = ["update", "-y", "--noninteractive", ScopeFlag(p)];
        if (p.Version is not null) args.Add($"--commit={p.Version}");
        args.AddRange(extra);
        args.Add(p.Identifier);
        return args;
    }

    public static List<string> SnapArguments(bool installed, InstallRepoPayload p, IReadOnlyList<string> extra)
    {
        // Bei Snap entspricht eine "Version" einer Revisionsnummer.
        List<string> args = [installed ? "refresh" : "install", p.Identifier];
        if (p.Channel is not null) args.Add($"--channel={p.Channel}");
        if (p.Version is not null) args.Add($"--revision={p.Version}");
        if (p.Classic && !installed) args.Add("--classic");
        args.AddRange(extra);
        return args;
    }

    // ---- Deinstallation ----

    public static List<string> WingetUninstallArguments(InstallRepoPayload p) =>
        ["uninstall", "--id", p.Identifier, "--exact", "--silent", "--disable-interactivity", "--accept-source-agreements"];

    public static List<string> ChocolateyUninstallArguments(InstallRepoPayload p) =>
        ["uninstall", p.Identifier, "-y", "--no-progress"];

    public static List<string> AptUninstallArguments(InstallRepoPayload p) => ["remove", "-y", "-q", p.Identifier];

    public static List<string> RpmUninstallArguments(InstallRepoPayload p) => ["-y", "remove", p.Identifier];

    public static List<string> FlatpakUninstallArguments(InstallRepoPayload p) =>
        ["uninstall", "-y", "--noninteractive", ScopeFlag(p), p.Identifier];

    public static List<string> SnapUninstallArguments(InstallRepoPayload p) => ["remove", p.Identifier];

    private static async Task<RepoResult> UninstallAsync(InstallRepoPayload p, TimeSpan timeout, CancellationToken ct)
    {
        switch (p.Manager)
        {
            case "Winget":
            {
                var exe = ToolLocator.Winget();
                return exe is null
                    ? Fail("winget wurde nicht gefunden (App-Installer fehlt oder ist für das SYSTEM-Konto nicht verfügbar).")
                    : Summarize(await ProcessRunner.RunAsync(exe, WingetUninstallArguments(p), timeout, ct), WingetRemoveOk);
            }
            case "Chocolatey":
            {
                var exe = ToolLocator.Chocolatey();
                return exe is null
                    ? Fail("Chocolatey ist auf diesem Gerät nicht installiert.")
                    : Summarize(await ProcessRunner.RunAsync(exe, ChocolateyUninstallArguments(p), timeout, ct), ChocolateyOk);
            }
            case "Apt":
            {
                var apt = ToolLocator.Linux("apt-get");
                return apt is null
                    ? Fail("apt-get ist auf diesem Gerät nicht vorhanden (keine DEB-basierte Distribution).")
                    : Summarize(await ProcessRunner.RunAsync(apt, AptUninstallArguments(p), timeout, ct, AptEnvironment), [0]);
            }
            case "Rpm":
            {
                var manager = ToolLocator.Linux("dnf") ?? ToolLocator.Linux("yum");
                return manager is null
                    ? Fail("dnf/yum ist auf diesem Gerät nicht vorhanden (keine RPM-basierte Distribution).")
                    : Summarize(await ProcessRunner.RunAsync(manager, RpmUninstallArguments(p), timeout, ct), [0]);
            }
            case "Flatpak":
            {
                var flatpak = ToolLocator.Linux("flatpak");
                return flatpak is null
                    ? Fail("Flatpak ist auf diesem Gerät nicht installiert.")
                    : Summarize(await ProcessRunner.RunAsync(flatpak, FlatpakUninstallArguments(p), timeout, ct), [0]);
            }
            case "Snap":
            {
                var snap = ToolLocator.Linux("snap");
                return snap is null
                    ? Fail("snap ist auf diesem Gerät nicht installiert.")
                    : Summarize(await ProcessRunner.RunAsync(snap, SnapUninstallArguments(p), timeout, ct), [0]);
            }
            default:
                return Fail($"Unbekannter Paketmanager '{p.Manager}'.");
        }
    }

    private static string ScopeFlag(InstallRepoPayload p) => p.Scope == "user" ? "--user" : "--system";

    // ---- Ausführung ----

    private static async Task<RepoResult> WingetAsync(InstallRepoPayload p, IReadOnlyList<string> extra, TimeSpan timeout, CancellationToken ct)
    {
        var exe = ToolLocator.Winget();
        return exe is null
            ? Fail("winget wurde nicht gefunden (App-Installer fehlt oder ist für das SYSTEM-Konto nicht verfügbar).")
            : Summarize(await ProcessRunner.RunAsync(exe, WingetArguments(p, extra), timeout, ct), WingetOk);
    }

    private static async Task<RepoResult> ChocolateyAsync(InstallRepoPayload p, IReadOnlyList<string> extra, TimeSpan timeout, CancellationToken ct)
    {
        var exe = ToolLocator.Chocolatey();
        return exe is null
            ? Fail("Chocolatey ist auf diesem Gerät nicht installiert.")
            : Summarize(await ProcessRunner.RunAsync(exe, ChocolateyArguments(p, extra), timeout, ct), ChocolateyOk);
    }

    private static async Task<RepoResult> AptAsync(InstallRepoPayload p, IReadOnlyList<string> extra, TimeSpan timeout, CancellationToken ct)
    {
        var apt = ToolLocator.Linux("apt-get");
        if (apt is null)
        {
            return Fail("apt-get ist auf diesem Gerät nicht vorhanden (keine DEB-basierte Distribution).");
        }

        var update = await ProcessRunner.RunAsync(apt, ["update", "-q"], timeout, ct, AptEnvironment);
        if (update.ExitCode != 0)
        {
            return Summarize(update, [0], "apt-get update");
        }

        return Summarize(await ProcessRunner.RunAsync(apt, AptArguments(p, extra), timeout, ct, AptEnvironment), [0]);
    }

    private static async Task<RepoResult> RpmAsync(InstallRepoPayload p, IReadOnlyList<string> extra, TimeSpan timeout, CancellationToken ct)
    {
        var manager = ToolLocator.Linux("dnf") ?? ToolLocator.Linux("yum");
        var rpm = ToolLocator.Linux("rpm");
        if (manager is null || rpm is null)
        {
            return Fail("dnf/yum und rpm sind auf diesem Gerät nicht vorhanden (keine RPM-basierte Distribution).");
        }

        var installed = (await ProcessRunner.RunAsync(rpm, ["-q", p.Identifier], TimeSpan.FromMinutes(2), ct)).ExitCode == 0;
        var name = Path.GetFileName(manager);
        return Summarize(await ProcessRunner.RunAsync(manager, RpmArguments(name, installed, p, extra), timeout, ct), [0]);
    }

    private static async Task<RepoResult> FlatpakAsync(InstallRepoPayload p, IReadOnlyList<string> extra, TimeSpan timeout, CancellationToken ct)
    {
        var flatpak = ToolLocator.Linux("flatpak");
        if (flatpak is null)
        {
            return Fail("Flatpak ist auf diesem Gerät nicht installiert.");
        }

        var installed = (await ProcessRunner.RunAsync(
            flatpak, ["info", ScopeFlag(p), p.Identifier], TimeSpan.FromMinutes(2), ct)).ExitCode == 0;

        if (installed)
        {
            return Summarize(await ProcessRunner.RunAsync(flatpak, FlatpakUpdateArguments(p, extra), timeout, ct), [0]);
        }

        var install = await ProcessRunner.RunAsync(flatpak, FlatpakInstallArguments(p, extra), timeout, ct);
        if (install.ExitCode != 0 || p.Version is null)
        {
            return Summarize(install, [0]);
        }

        // Feste Version (Commit): nach der Installation auf diesen Stand bringen
        return Summarize(await ProcessRunner.RunAsync(flatpak, FlatpakUpdateArguments(p, []), timeout, ct), [0]);
    }

    private static async Task<RepoResult> SnapAsync(InstallRepoPayload p, IReadOnlyList<string> extra, TimeSpan timeout, CancellationToken ct)
    {
        var snap = ToolLocator.Linux("snap");
        if (snap is null)
        {
            return Fail("snap ist auf diesem Gerät nicht installiert.");
        }

        var installed = (await ProcessRunner.RunAsync(snap, ["list", p.Identifier], TimeSpan.FromMinutes(2), ct)).ExitCode == 0;
        return Summarize(await ProcessRunner.RunAsync(snap, SnapArguments(installed, p, extra), timeout, ct), [0]);
    }

    private static RepoResult Summarize(ProcessRunner.Result result, IReadOnlyCollection<int> successCodes, string? step = null)
    {
        var prefix = step is null ? "" : $"{step}: ";
        if (result.TimedOut)
        {
            return Fail($"{prefix}Zeitüberschreitung.\n{Tail(result)}");
        }

        var ok = successCodes.Contains(result.ExitCode);
        return new RepoResult(ok, $"{prefix}Exit-Code {result.ExitCode}{(ok ? "" : " (Fehler)")}\n{Tail(result)}".TrimEnd());
    }

    private static string Tail(ProcessRunner.Result result)
    {
        var text = (result.Output + "\n" + result.Error).Trim();
        return text.Length <= 60000 ? text : text[^60000..];
    }

    private static RepoResult Fail(string message) => new(false, message);
}
