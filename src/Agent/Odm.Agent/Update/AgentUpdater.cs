using System.Diagnostics;
using System.IO.Compression;
using System.Formats.Tar;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Odm.Agent.Update;

/// <summary>
/// Self-update: downloads the package offered by the server, verifies the SHA-256 checksum, unpacks it into a
/// staging directory and hands the replacement to a helper script (with rollback). It only updates when the
/// server makes an offer and the agent is not processing any jobs.
/// </summary>
public sealed partial class AgentUpdater(HttpClient http, AgentStateStore store, ILogger<AgentUpdater> logger)
{
    private const long MaxPackageBytes = 500L * 1024 * 1024;
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(10);

    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;

    [GeneratedRegex(@"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.\-]+)?$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^api/agent/download/(win|linux)-(x64|arm64)(\?version=[0-9A-Za-z.\-]+)?$")]
    private static partial Regex UrlPattern();

    [GeneratedRegex("^[0-9a-fA-F]{64}$")]
    private static partial Regex Sha256Pattern();

    /// <summary>Platform identifier as in the package name, e.g. <c>linux-x64</c>.</summary>
    public static string? Platform
    {
        get
        {
            var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsLinux() ? "linux" : null;
            var arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                _ => null
            };
            return os is null || arch is null ? null : $"{os}-{arch}";
        }
    }

    private string UpdatesDirectory => Path.Combine(store.StateDirectory, "updates");
    private string PendingFile => Path.Combine(store.StateDirectory, "update-pending");
    private string OkFile => Path.Combine(store.StateDirectory, "update-ok");
    private string FailedFile => Path.Combine(store.StateDirectory, "update-failed");

    /// <summary>
    /// Called after the first successful heartbeat: tells the helper script that the new version is running.
    /// </summary>
    public void ConfirmStartup()
    {
        try
        {
            if (!File.Exists(PendingFile))
            {
                return;
            }

            // Only the new version confirms; the old agent keeps running briefly and must not delete the marker.
            var pending = File.ReadAllText(PendingFile).Trim();
            if (pending == SystemInfoProvider.AgentVersion)
            {
                File.WriteAllText(OkFile, pending);
                File.Delete(PendingFile);
                logger.LogInformation("Update to version {Version} completed.", pending);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The update status could not be saved.");
        }
    }

    /// <summary>Returns true if an update was started (the service will be restarted shortly).</summary>
    public async Task<bool> TryUpdateAsync(UpdateOffer offer, CancellationToken ct)
    {
        if (DateTimeOffset.UtcNow - _lastAttempt < RetryAfter)
        {
            return false;
        }

        if (!Valid(offer) || offer.Version == SystemInfoProvider.AgentVersion || FailedVersion() == offer.Version)
        {
            return false;
        }

        _lastAttempt = DateTimeOffset.UtcNow;
        var stage = Path.Combine(UpdatesDirectory, offer.Version);
        try
        {
            logger.LogInformation("Downloading update from {Old} to {New}.", SystemInfoProvider.AgentVersion, offer.Version);
            Directory.CreateDirectory(UpdatesDirectory);
            if (Directory.Exists(stage))
            {
                Directory.Delete(stage, recursive: true);
            }

            var archive = Path.Combine(UpdatesDirectory, $"package-{offer.Version}.download");
            try
            {
                await DownloadAsync(offer, archive, ct);
                Extract(archive, stage);
            }
            finally
            {
                File.Delete(archive);
            }

            var binary = Path.Combine(stage, OperatingSystem.IsWindows() ? "odm-agent.exe" : "odm-agent");
            if (!File.Exists(binary))
            {
                throw new InvalidDataException("The package contains no program file.");
            }

            File.WriteAllText(PendingFile, offer.Version);
            Launch(stage, offer.Version);
            logger.LogInformation("Update script started; the agent is restarting.");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The update to {Version} failed.", offer.Version);
            TryDelete(PendingFile);
            TryDeleteDirectory(stage);
            return false;
        }
    }

    private static bool Valid(UpdateOffer offer) =>
        VersionPattern().IsMatch(offer.Version) &&
        Sha256Pattern().IsMatch(offer.Sha256) &&
        UrlPattern().IsMatch(offer.Url) &&
        offer.Size is > 0 and <= MaxPackageBytes;

    private string? FailedVersion()
    {
        try
        {
            return File.Exists(FailedFile) ? File.ReadAllText(FailedFile).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private async Task DownloadAsync(UpdateOffer offer, string destination, CancellationToken ct)
    {
        using var response = await http.GetAsync(offer.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var target = File.Create(destination))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (total > MaxPackageBytes)
                {
                    throw new InvalidDataException("The package is larger than allowed.");
                }

                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }

        var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (total != offer.Size || !string.Equals(actual, offer.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The size or checksum of the package does not match the offer.");
        }
    }

    private static void Extract(string archive, string destination)
    {
        Directory.CreateDirectory(destination);
        if (OperatingSystem.IsWindows())
        {
            ZipFile.ExtractToDirectory(archive, destination, overwriteFiles: true);
            return;
        }

        using var file = File.OpenRead(archive);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        TarFile.ExtractToDirectory(gzip, destination, overwriteFiles: true);
    }

    private void Launch(string stage, string version)
    {
        var install = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(UpdatesDirectory, "update.ps1");
            File.WriteAllText(script, UpdateScripts.Windows);
            var info = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var arg in new[]
                     {
                         "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
                         "-Install", install, "-Stage", stage, "-State", store.StateDirectory, "-Version", version
                     })
            {
                info.ArgumentList.Add(arg);
            }

            Process.Start(info)?.Dispose();
            return;
        }

        var shell = Path.Combine(UpdatesDirectory, "update.sh");
        File.WriteAllText(shell, UpdateScripts.Linux);
        File.SetUnixFileMode(shell, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        // Own transient systemd unit: the script survives the agent service being stopped.
        var run = new ProcessStartInfo("systemd-run") { UseShellExecute = false };
        foreach (var arg in new[]
                 {
                     "--unit=odm-agent-update", "--collect", "--no-block", "/bin/sh", shell, install, stage, store.StateDirectory, version
                 })
        {
            run.ArgumentList.Add(arg);
        }

        using var process = Process.Start(run) ?? throw new InvalidOperationException("systemd-run could not be started.");
        process.WaitForExit(15_000);
        if (process.HasExited && process.ExitCode != 0)
        {
            throw new InvalidOperationException($"systemd-run exited with code {process.ExitCode}.");
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
