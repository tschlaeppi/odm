using System.Text.RegularExpressions;
using Odm.Modules.Software.Domain;

namespace Odm.Modules.Software.Application;

/// <summary>
/// Validates and normalizes package settings. Identifiers and versions always start with a letter or
/// a digit so that they cannot be interpreted as an option of the package manager.
/// </summary>
public static partial class PackageSpecValidator
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+:@/\-]{0,199}$")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+:~\-]{0,63}$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._\-]{0,63}$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._:/\-]{0,199}$")]
    private static partial Regex SourceUrlPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._/\-]{0,63}$")]
    private static partial Regex ChannelPattern();

    public const int MaxArgumentsLength = 1000;

    public static bool TryValidate(
        PackageKind kind,
        PackageSpecDto? dto,
        IReadOnlySet<string> knownSecrets,
        out PackageSpec spec,
        out Dictionary<string, string[]> errors)
    {
        errors = [];
        dto ??= new PackageSpecDto(null, null, null, null, null, null, false, null);

        var identifier = Clean(dto.Identifier);
        var version = Clean(dto.Version);
        var scope = Clean(dto.Scope)?.ToLowerInvariant();
        var source = Clean(dto.Source);
        var remote = Clean(dto.Remote);
        var channel = Clean(dto.Channel);
        var arguments = Clean(dto.Arguments);
        string? platform = null;
        if (kind == PackageKind.Psadt)
        {
            var requested = Clean(dto.Platform);
            if (requested is null || string.Equals(requested, "Windows", StringComparison.OrdinalIgnoreCase))
            {
                platform = null;
            }
            else if (string.Equals(requested, PackageSpec.LinuxPlatform, StringComparison.OrdinalIgnoreCase))
            {
                platform = PackageSpec.LinuxPlatform;
            }
            else
            {
                errors["platform"] = ["Platform: Windows or Linux."];
            }
        }

        if (kind.IsRepo())
        {
            if (identifier is null || !IdentifierPattern().IsMatch(identifier))
                errors["identifier"] = ["Package name/ID is required (letters, digits and . _ + : @ / -)."];
            if (version is not null && !VersionPattern().IsMatch(version))
                errors["version"] = ["Invalid version."];
        }
        else
        {
            identifier = null;
            version = null;
        }

        switch (kind)
        {
            case PackageKind.Winget:
                scope ??= "machine";
                if (scope is not ("machine" or "user")) errors["scope"] = ["Scope: machine or user."];
                if (source is not null && !NamePattern().IsMatch(source)) errors["source"] = ["Invalid source."];
                remote = channel = null;
                break;
            case PackageKind.Chocolatey:
                scope = remote = channel = null;
                if (source is not null && !SourceUrlPattern().IsMatch(source)) errors["source"] = ["Invalid source."];
                break;
            case PackageKind.Flatpak:
                scope ??= "system";
                if (scope is not ("system" or "user")) errors["scope"] = ["Scope: system or user."];
                remote ??= "flathub";
                if (!NamePattern().IsMatch(remote)) errors["remote"] = ["Invalid remote."];
                source = channel = null;
                break;
            case PackageKind.Snap:
                channel ??= "stable";
                if (!ChannelPattern().IsMatch(channel)) errors["channel"] = ["Invalid channel."];
                scope = source = remote = null;
                break;
            default: // Psadt, Apt, Rpm
                scope = source = remote = channel = null;
                break;
        }

        var classic = kind == PackageKind.Snap && dto.Classic;

        if (arguments is not null)
        {
            if (arguments.Length > MaxArgumentsLength)
            {
                errors["arguments"] = [$"At most {MaxArgumentsLength} characters."];
            }
            else
            {
                var unknown = SecretReferences.Extract(arguments).Where(n => !knownSecrets.Contains(n)).ToList();
                if (unknown.Count > 0)
                {
                    errors["arguments"] = [$"Unknown secret: {string.Join(", ", unknown)}."];
                }
            }
        }

        spec = new PackageSpec(identifier, version, scope, source, remote, channel, classic, arguments, platform);
        return errors.Count == 0;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
