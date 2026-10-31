using System.Text.Json;

namespace Odm.Agent.Os;

/// <summary>
/// Windows-Updates über die Windows-Update-Agent-API (COM, Microsoft.Update.Session) – ohne Zusatzmodule.
/// Läuft als SYSTEM; "SecurityOnly" installiert Sicherheits- und kritische Updates.
/// </summary>
public sealed class WindowsUpdater : IOsUpdater
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string Script = """
        param([string]$Mode = 'All')
        $ErrorActionPreference = 'Stop'
        $session = New-Object -ComObject Microsoft.Update.Session
        $searcher = $session.CreateUpdateSearcher()
        $found = $searcher.Search("IsInstalled=0 and IsHidden=0 and Type='Software'")
        $selected = New-Object -ComObject Microsoft.Update.UpdateColl
        $titles = @()
        foreach ($u in $found.Updates) {
            if ($u.InstallationBehavior.CanRequestUserInput) { continue }
            if ($Mode -eq 'SecurityOnly') {
                $isSecurity = $false
                foreach ($c in $u.Categories) {
                    if ($c.Name -eq 'Security Updates' -or $c.Name -eq 'Critical Updates') { $isSecurity = $true }
                }
                if (-not $isSecurity) { continue }
            }
            if (-not $u.EulaAccepted) { $u.AcceptEula() }
            [void]$selected.Add($u)
            $titles += $u.Title
        }
        $installed = 0
        $failed = 0
        $reboot = $false
        if ($selected.Count -gt 0) {
            $downloader = $session.CreateUpdateDownloader()
            $downloader.Updates = $selected
            [void]$downloader.Download()
            $installer = $session.CreateUpdateInstaller()
            $installer.Updates = $selected
            $result = $installer.Install()
            $reboot = [bool]$result.RebootRequired
            for ($i = 0; $i -lt $selected.Count; $i++) {
                if ($result.GetUpdateResult($i).ResultCode -eq 2) { $installed++ } else { $failed++ }
            }
        }
        if (-not $reboot) {
            $reboot = (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired') -or
                      (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending')
        }
        @{ installed = $installed; failed = $failed; titles = @($titles); rebootRequired = $reboot } | ConvertTo-Json -Compress
        """;

    private sealed record ScriptOutput(int Installed, int Failed, string[]? Titles, bool RebootRequired);

    public async Task<OsUpdateResult> RunAsync(OsUpdatePayload payload, string workDirectory, CancellationToken ct)
    {
        var scriptPath = Path.Combine(workDirectory, "update.ps1");
        await File.WriteAllTextAsync(scriptPath, Script, ct);

        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var timeout = TimeSpan.FromMinutes(Math.Clamp(payload.TimeoutMinutes, 5, 24 * 60));

        var result = await ProcessRunner.RunAsync(
            powershell,
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath, "-Mode", payload.Mode],
            timeout, ct);

        if (result.TimedOut)
        {
            return new OsUpdateResult(false, 0, 0, [], false, $"Timed out after {timeout.TotalMinutes:N0} minutes.");
        }

        if (result.ExitCode != 0)
        {
            return new OsUpdateResult(false, 0, 0, [], false,
                $"Windows Update failed (exit code {result.ExitCode}). {Tail(result.Error.Length > 0 ? result.Error : result.Output)}");
        }

        // Die letzte JSON-Zeile der Ausgabe ist das Ergebnis; davor können Warnungen stehen.
        var line = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(l => l.StartsWith('{'));
        var parsed = line is null ? null : JsonSerializer.Deserialize<ScriptOutput>(line, Json);
        if (parsed is null)
        {
            return new OsUpdateResult(false, 0, 0, [], false, "The output of the update script could not be read.");
        }

        return new OsUpdateResult(
            parsed.Failed == 0, parsed.Installed, parsed.Failed, parsed.Titles ?? [], parsed.RebootRequired,
            parsed.Failed > 0 ? $"{parsed.Failed} update(s) could not be installed." : null);
    }

    private static string Tail(string text) => text.Length <= 500 ? text.Trim() : text[^500..].Trim();
}
