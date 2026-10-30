using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Odm.Agent;

/// <summary>Zugangsdaten, die nach der Registrierung lokal gespeichert werden.</summary>
public sealed record AgentState(Guid AgentId, string Secret, Guid DeviceId);

/// <summary>
/// Speichert den Agent-Zustand. Die Datei enthält das Agent-Secret und ist deshalb nur für
/// den Dienstkonto-Kontext lesbar (Linux: 0600/0700, Windows: nur SYSTEM und Administratoren).
/// </summary>
public sealed class AgentStateStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _directory;

    /// <summary>Zustandsverzeichnis (Zugangsdaten, Logs, Update-Dateien).</summary>
    public string StateDirectory => _directory;
    private readonly string _file;

    public AgentStateStore(IOptions<AgentOptions> options)
    {
        _directory = string.IsNullOrWhiteSpace(options.Value.StateDirectory)
            ? DefaultDirectory()
            : options.Value.StateDirectory;
        _file = Path.Combine(_directory, "agent-state.json");
    }

    public async Task<AgentState?> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_file))
        {
            return null;
        }

        await using var stream = File.OpenRead(_file);
        return await JsonSerializer.DeserializeAsync<AgentState>(stream, Json, ct);
    }

    public async Task SaveAsync(AgentState state, CancellationToken ct)
    {
        Directory.CreateDirectory(_directory);
        Restrict(_directory);

        // Atomar schreiben: erst temporär, dann ersetzen.
        var temp = _file + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(state, Json), ct);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(temp, _file, overwrite: true);
    }

    /// <summary>
    /// Legt ein Arbeitsverzeichnis für einen Auftrag an. Es liegt unterhalb des geschützten Zustandsverzeichnisses,
    /// nicht im öffentlich beschreibbaren Temp-Ordner: sonst könnten lokale Benutzer Paketdateien austauschen,
    /// die anschliessend mit SYSTEM-Rechten ausgeführt werden.
    /// </summary>
    public string CreateWorkDirectory(Guid jobId)
    {
        Directory.CreateDirectory(_directory);
        Restrict(_directory);

        var path = Path.Combine(_directory, "work", jobId.ToString("N"));
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Standard-Zustandsverzeichnis je Betriebssystem (vom Installationsskript angelegt und geschützt).</summary>
    public static string DefaultDirectory() =>
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OpenDeviceManager")
            : "/var/lib/odm-agent";

    /// <summary>
    /// Konfigurationsdatei des Installationsskripts (Agent:ServerUrl und das einmalige Agent:RegistrationToken).
    /// Sie liegt im geschützten Zustandsverzeichnis, nicht neben dem Programm, damit normale Benutzer das Token nicht lesen können.
    /// </summary>
    public static string ConfigFile => Path.Combine(DefaultDirectory(), "agent.json");

    /// <summary>
    /// Entfernt das Registrierungs-Token aus der Konfigurationsdatei. Es ist nach der Registrierung wertlos
    /// (Einmal-Token) und soll nicht länger auf dem Gerät liegen.
    /// </summary>
    public async Task ClearRegistrationTokenAsync(CancellationToken ct)
    {
        var path = ConfigFile;
        if (!File.Exists(path))
        {
            return;
        }

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path, ct)) as JsonObject;
        if (root?["Agent"] is not JsonObject agent || !agent.Remove("RegistrationToken"))
        {
            return;
        }

        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, root.ToJsonString(Json), ct);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(temp, path, overwrite: true);
    }

    private static void Restrict(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            RestrictWindows(directory);
        }
        else
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void RestrictWindows(string directory)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        new DirectoryInfo(directory).SetAccessControl(security);
    }
}
