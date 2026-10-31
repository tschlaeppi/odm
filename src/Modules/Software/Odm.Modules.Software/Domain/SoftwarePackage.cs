using System.Text.Json;
using Odm.SharedKernel;

namespace Odm.Modules.Software.Domain;

public enum PackageKind
{
    /// <summary>PSAppDeployToolkit package (zipped PSADT), Windows only.</summary>
    Psadt = 0,

    // Packages from third-party repositories: the device's package manager installs and updates them.
    Winget = 1,
    Chocolatey = 2,
    Apt = 3,
    Rpm = 4,
    Flatpak = 5,
    Snap = 6
}

public static class PackageKindExtensions
{
    public static bool IsRepo(this PackageKind kind) => kind != PackageKind.Psadt;

    /// <summary>Winget, Chocolatey and PSADT run on Windows only, the rest on Linux only.</summary>
    public static bool IsWindowsOnly(this PackageKind kind) =>
        kind is PackageKind.Psadt or PackageKind.Winget or PackageKind.Chocolatey;
}

/// <summary>
/// Package settings. Depending on the kind only some fields are relevant; irrelevant ones stay empty
/// so that equal settings always lead to the same JSON (change detection).
/// </summary>
public sealed record PackageSpec(
    string? Identifier,
    string? Version,
    string? Scope,
    string? Source,
    string? Remote,
    string? Channel,
    bool Classic,
    string? Arguments)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static PackageSpec Empty { get; } = new(null, null, null, null, null, null, false, null);

    /// <summary>Repo packages without a pinned version are updated automatically by the package manager.</summary>
    public bool AutoUpdate => Version is null;

    public static PackageSpec Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<PackageSpec>(json, Json) ?? Empty;
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);
}

public sealed class SoftwarePackage : Entity
{
    public string Name { get; private set; } = "";
    public string? Description { get; private set; }
    public PackageKind Kind { get; private set; }
    public string? SpecJson { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private SoftwarePackage() { }

    public static SoftwarePackage Create(string name, string? description, PackageKind kind, PackageSpec spec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new SoftwarePackage
        {
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Kind = kind,
            SpecJson = spec.ToJson(),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public PackageSpec Spec => PackageSpec.Parse(SpecJson);

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public void Update(string? description, PackageSpec spec)
    {
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        SpecJson = spec.ToJson();
    }
}

/// <summary>
/// Eine Version eines Pakets. PSADT: hochgeladenes ZIP ("neueste Version" = zuletzt hochgeladene).
/// Repo packages: a "revision", i.e. a snapshot of the settings; when the configuration changes,
/// a new revision is created and the devices are reconciled to it.
/// </summary>
public sealed class PackageVersion : Entity
{
    public Guid PackageId { get; private set; }
    public string Version { get; private set; } = "";
    public string? ObjectKey { get; private set; }
    public long SizeBytes { get; private set; }
    public string? Sha256 { get; private set; }
    public string? SpecSnapshot { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }

    private PackageVersion() { }

    public static PackageVersion Create(
        Guid id, Guid packageId, string version, string objectKey, long sizeBytes, string sha256, string? notes)
    {
        var created = new PackageVersion
        {
            PackageId = packageId,
            Version = version.Trim(),
            ObjectKey = objectKey,
            SizeBytes = sizeBytes,
            Sha256 = sha256.ToLowerInvariant(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            UploadedAt = DateTimeOffset.UtcNow
        };
        created.SetId(id);
        return created;
    }

    public static PackageVersion CreateRevision(Guid packageId, string label, PackageSpec spec) => new()
    {
        PackageId = packageId,
        Version = label,
        SpecSnapshot = spec.ToJson(),
        UploadedAt = DateTimeOffset.UtcNow
    };

    private void SetId(Guid id) => Id = id;
}
