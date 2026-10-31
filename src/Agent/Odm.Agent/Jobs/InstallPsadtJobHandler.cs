using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Odm.Agent.Repo;

namespace Odm.Agent.Jobs;

/// <summary>Content of the job (counterpart of the server).</summary>
public sealed record InstallPsadtPayload(
    Guid VersionId, string PackageName, string Version, string Sha256, long SizeBytes, int TimeoutMinutes,
    string? Arguments = null, bool Uninstall = false);

/// <summary>
/// Installs (or uninstalls) a PSADT package: download, verify checksum, unpack, run the script silently.
/// Windows: PSADT v4 (Invoke-AppDeployToolkit.ps1) and v3 (Deploy-Application.ps1) with Windows PowerShell 5.1.
/// Linux: PSAppDeployToolkit-Linux packages (v4 script) with PowerShell 7.4+ (pwsh).
/// </summary>
public sealed class InstallPsadtJobHandler(ILogger<InstallPsadtJobHandler> logger) : IJobHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Scripts = ["Invoke-AppDeployToolkit.ps1", "Deploy-Application.ps1"];

    // 1641 and 3010: installation succeeded, restart required.
    private static readonly HashSet<int> WindowsSuccessCodes = [0, 1641, 3010];

    // Linux passes only the low 8 bits of an exit code to the parent: 1641 -> 105, 3010 -> 194.
    private static readonly HashSet<int> LinuxSuccessCodes = [0, 1641 & 0xFF, 3010 & 0xFF];

    private static readonly string[] PwshLocations =
        ["/usr/bin/pwsh", "/usr/local/bin/pwsh", "/opt/microsoft/powershell/7/pwsh", "/snap/bin/pwsh"];

    public string Type => "install-psadt";

    public async Task<JobOutcome> HandleAsync(ServerJob job, JobContext context, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            return Fail("PSADT packages are only supported on Windows and Linux.");
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

            if (OperatingSystem.IsLinux() && !Path.GetFileName(script).Equals(Scripts[0], StringComparison.OrdinalIgnoreCase))
            {
                return Fail("Linux needs a PSAppDeployToolkit-Linux package (Invoke-AppDeployToolkit.ps1).");
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
        // Root directory first, then one level below (ZIPs often contain a folder).
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
        var linux = OperatingSystem.IsLinux();
        string powershell;
        if (linux)
        {
            var pwsh = PwshLocations.FirstOrDefault(File.Exists);
            if (pwsh is null)
            {
                return Fail("PowerShell 7.4 or newer (pwsh) is not installed on this device.");
            }

            powershell = pwsh;
        }
        else
        {
            powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        }

        var startInfo = new ProcessStartInfo(powershell)
        {
            WorkingDirectory = Path.GetDirectoryName(script)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        string[] fixedArguments = linux
            ? ["-NoProfile", "-NonInteractive", "-File", script, "-DeploymentType", deploymentType, "-DeployMode", "Silent"]
            : ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-DeploymentType", deploymentType, "-DeployMode", "Silent"];
        foreach (var argument in fixedArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (linux)
        {
            // A systemd service has no HOME; pwsh and PSADT need one for caches and logs.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HOME")))
            {
                startInfo.Environment["HOME"] = "/root";
            }

            startInfo.Environment["POWERSHELL_TELEMETRY_OPTOUT"] = "1";
            startInfo.Environment["POWERSHELL_UPDATECHECK"] = "Off";
            startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        }

        // Additional arguments are passed to the PSADT script; the script must declare matching parameters.
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
                throw; // agent is shutting down: do not report the job as failed, it will be delivered again.
            }

            return Fail($"Timed out after {minutes} minutes.\n{SecretSubstitution.Redact(tail.ToString(), arguments.SecretValues)}");
        }

        process.WaitForExit(); // makes sure the last output has been read

        var code = process.ExitCode;
        var successCodes = linux ? LinuxSuccessCodes : WindowsSuccessCodes;
        var summary = code switch
        {
            0 => "Exit code 0 (succeeded)",
            1641 or 3010 => $"Exit code {code} (succeeded, restart required)",
            105 or 194 when linux => $"Exit code {code} (succeeded, restart required)",
            _ => $"Exit code {code}"
        };

        return new JobOutcome(
            successCodes.Contains(code),
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
            // process has already exited
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
            // Cleaning up is best effort; leftovers stay in the protected working directory.
        }
    }

    /// <summary>Keeps only the end of the output (that is where the relevant lines are on errors).</summary>
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
