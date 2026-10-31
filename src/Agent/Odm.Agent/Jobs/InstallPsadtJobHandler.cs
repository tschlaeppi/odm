using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Odm.Agent.Repo;

namespace Odm.Agent.Jobs;

/// <summary>Inhalt des Auftrags (Gegenstück zum Server).</summary>
public sealed record InstallPsadtPayload(
    Guid VersionId, string PackageName, string Version, string Sha256, long SizeBytes, int TimeoutMinutes,
    string? Arguments = null, bool Uninstall = false);

/// <summary>
/// Installiert (oder deinstalliert) ein PSADT-Paket: herunterladen, Prüfsumme prüfen, entpacken, Skript still ausführen.
/// Unterstützt PSADT v4 (Invoke-AppDeployToolkit.ps1) und v3 (Deploy-Application.ps1).
/// </summary>
public sealed class InstallPsadtJobHandler(ILogger<InstallPsadtJobHandler> logger) : IJobHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Scripts = ["Invoke-AppDeployToolkit.ps1", "Deploy-Application.ps1"];

    // 1641 und 3010: Installation erfolgreich, Neustart erforderlich.
    private static readonly HashSet<int> SuccessCodes = [0, 1641, 3010];

    public string Type => "install-psadt";

    public async Task<JobOutcome> HandleAsync(ServerJob job, JobContext context, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Fail("PSADT packages are only supported on Windows.");
        }

        InstallPsadtPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<InstallPsadtPayload>(job.Payload ?? "", Json);
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null || payload.VersionId == Guid.Empty || string.IsNullOrWhiteSpace(payload.Sha256))
        {
            return Fail("Invalid job content.");
        }

        var workDirectory = context.Store.CreateWorkDirectory(job.Id);
        try
        {
            var zipPath = Path.Combine(workDirectory, "package.zip");
            logger.LogInformation("Downloading {Package} {Version}.", payload.PackageName, payload.Version);
            await context.Server.DownloadPackageAsync(context.State, payload.VersionId, zipPath, ct);

            await using (var stream = File.OpenRead(zipPath))
            {
                var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
                if (!string.Equals(actual, payload.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return Fail("The checksum of the downloaded package does not match.");
                }
            }

            var extractDirectory = Path.Combine(workDirectory, "package");
            ZipFile.ExtractToDirectory(zipPath, extractDirectory);

            var script = FindScript(extractDirectory);
            if (script is null)
            {
                return Fail("No PSADT script was found in the package.");
            }

            ResolvedArguments arguments;
            try
            {
                arguments = await ResolvedArguments.ResolveAsync(payload.Arguments, job, context, ct);
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException)
            {
                return Fail(ex.Message);
            }

            logger.LogInformation("{Action} {Package} {Version}.", payload.Uninstall ? "Uninstalling" : "Installing", payload.PackageName, payload.Version);
            return await RunAsync(script, payload.TimeoutMinutes, arguments, payload.Uninstall ? "Uninstall" : "Install", ct);
        }
        finally
        {
            TryDelete(workDirectory);
        }
    }

    private static string? FindScript(string root)
    {
        // Stammverzeichnis zuerst, dann eine Ebene darunter (ZIPs enthalten oft einen Ordner).
        var directories = new[] { root }.Concat(Directory.GetDirectories(root));
        foreach (var directory in directories)
        {
            foreach (var name in Scripts)
            {
                var path = Path.Combine(directory, name);
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    private static async Task<JobOutcome> RunAsync(
        string script, int timeoutMinutes, ResolvedArguments arguments, string deploymentType, CancellationToken ct)
    {
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var startInfo = new ProcessStartInfo(powershell)
        {
            WorkingDirectory = Path.GetDirectoryName(script)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in new[]
                 {
                     "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                     "-File", script, "-DeploymentType", deploymentType, "-DeployMode", "Silent"
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Zusatzargumente werden an das PSADT-Skript übergeben; das Skript muss entsprechende Parameter deklarieren.
        foreach (var argument in arguments.Tokens)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var tail = new TailBuffer(60000);
        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => tail.Append(e.Data);
        process.ErrorDataReceived += (_, e) => tail.Append(e.Data);

        var minutes = Math.Clamp(timeoutMinutes, 1, 24 * 60);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(minutes));

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (ct.IsCancellationRequested)
            {
                throw; // Agent wird beendet: Auftrag nicht als fehlgeschlagen melden, er wird erneut ausgeliefert.
            }

            return Fail($"Timed out after {minutes} minutes.\n{SecretSubstitution.Redact(tail.ToString(), arguments.SecretValues)}");
        }

        process.WaitForExit(); // stellt sicher, dass die letzte Ausgabe gelesen wurde

        var code = process.ExitCode;
        var summary = code switch
        {
            0 => "Exit code 0 (succeeded)",
            1641 or 3010 => $"Exit code {code} (succeeded, restart required)",
            _ => $"Exit code {code}"
        };

        return new JobOutcome(
            SuccessCodes.Contains(code),
            SecretSubstitution.Redact($"{summary}\n{tail}", arguments.SecretValues).TrimEnd());
    }

    private static JobOutcome Fail(string message) => new(false, message);

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Prozess ist bereits beendet
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception)
        {
            // Aufräumen ist best effort; Reste liegen im geschützten Arbeitsverzeichnis.
        }
    }

    /// <summary>Behält nur das Ende der Ausgabe (dort stehen bei Fehlern die relevanten Zeilen).</summary>
    private sealed class TailBuffer(int capacity)
    {
        private readonly StringBuilder _text = new();
        private readonly object _gate = new();

        public void Append(string? line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            lock (_gate)
            {
                _text.AppendLine(line);
                if (_text.Length > capacity)
                {
                    _text.Remove(0, _text.Length - capacity);
                }
            }
        }

        public override string ToString()
        {
            lock (_gate)
            {
                return _text.ToString();
            }
        }
    }
}
