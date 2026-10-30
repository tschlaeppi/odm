using System.Text.RegularExpressions;
using Odm.Modules.Software.Domain;

namespace Odm.Modules.Software.Application;

/// <summary>
/// Prüft und normalisiert Paketeinstellungen. Bezeichner und Versionen beginnen immer mit einem Buchstaben oder
/// einer Ziffer, damit sie nicht als Option des Paketmanagers gedeutet werden können.
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

        if (kind.IsRepo())
        {
            if (identifier is null || !IdentifierPattern().IsMatch(identifier))
                errors["identifier"] = ["Paketname/-ID ist erforderlich (Buchstaben, Ziffern und . _ + : @ / -)."];
            if (version is not null && !VersionPattern().IsMatch(version))
                errors["version"] = ["Ungültige Version."];
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
                if (scope is not ("machine" or "user")) errors["scope"] = ["Bereich: machine oder user."];
                if (source is not null && !NamePattern().IsMatch(source)) errors["source"] = ["Ungültige Quelle."];
                remote = channel = null;
                break;
            case PackageKind.Chocolatey:
                scope = remote = channel = null;
                if (source is not null && !SourceUrlPattern().IsMatch(source)) errors["source"] = ["Ungültige Quelle."];
                break;
            case PackageKind.Flatpak:
                scope ??= "system";
                if (scope is not ("system" or "user")) errors["scope"] = ["Bereich: system oder user."];
                remote ??= "flathub";
                if (!NamePattern().IsMatch(remote)) errors["remote"] = ["Ungültiges Remote."];
                source = channel = null;
                break;
            case PackageKind.Snap:
                channel ??= "stable";
                if (!ChannelPattern().IsMatch(channel)) errors["channel"] = ["Ungültiger Kanal."];
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
                errors["arguments"] = [$"Höchstens {MaxArgumentsLength} Zeichen."];
            }
            else
            {
                var unknown = SecretReferences.Extract(arguments).Where(n => !knownSecrets.Contains(n)).ToList();
                if (unknown.Count > 0)
                {
                    errors["arguments"] = [$"Unbekanntes Secret: {string.Join(", ", unknown)}."];
                }
            }
        }

        spec = new PackageSpec(identifier, version, scope, source, remote, channel, classic, arguments);
        return errors.Count == 0;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
