using System.Text.RegularExpressions;
using Odm.Modules.Identity.Domain;

namespace Odm.Modules.Identity.Infrastructure.Oidc;

/// <summary>
/// Anmeldung über OpenID Connect (Microsoft Entra ID, authentik, Keycloak, Google u. a.).
/// Mehrere Anbieter gleichzeitig sind möglich; jeder erscheint als eigener Knopf auf der Anmeldeseite.
/// </summary>
public sealed partial class OidcOptions
{
    public const string SectionName = "Identity:Oidc";

    [GeneratedRegex("^[a-z0-9-]{1,32}$")]
    private static partial Regex IdPattern();

    /// <summary>Öffentliche Adresse von ODM, z. B. https://odm.example.org. Daraus entsteht die Rückleite-Adresse (Redirect URI).</summary>
    public string PublicBaseUrl { get; set; } = "";

    public List<OidcProviderOptions> Providers { get; set; } = [];

    public string RedirectUri => PublicBaseUrl.TrimEnd('/') + "/api/auth/oidc/callback";

    public bool SecureCookies => PublicBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Beschreibt das erste Problem der Konfiguration oder liefert null. Ohne Anbieter ist alles in Ordnung.</summary>
    public string? Problem()
    {
        if (Providers.Count == 0)
        {
            return null;
        }

        if (!IsAllowedUrl(PublicBaseUrl) || new Uri(PublicBaseUrl).AbsolutePath.Trim('/').Length > 0)
        {
            return "PublicBaseUrl must be an https address without a path (http only for localhost).";
        }

        if (Providers.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != Providers.Count)
        {
            return "Provider IDs must be unique.";
        }

        foreach (var p in Providers)
        {
            if (p.Problem() is { } problem)
            {
                return $"Anbieter '{p.Id}': {problem}";
            }
        }

        return null;
    }

    /// <summary>Erlaubt https; http nur für localhost (Entwicklung).</summary>
    public static bool IsAllowedUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));

    internal static bool IsValidId(string id) => IdPattern().IsMatch(id);
}

public sealed class OidcProviderOptions
{
    private static readonly string[] DefaultScopes = ["openid", "profile", "email"];

    /// <summary>Kurzname für die Adresse, nur a–z, 0–9 und Bindestrich (z. B. "entra").</summary>
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Aussteller-Adresse. Entra: https://login.microsoftonline.com/&lt;Verzeichnis-ID&gt;/v2.0 (nicht "common");
    /// authentik: https://auth.example.org/application/o/&lt;slug&gt;/.
    /// </summary>
    public string Authority { get; set; } = "";

    public string ClientId { get; set; } = "";

    /// <summary>Leer = öffentlicher Client (nur PKCE). Für Entra und authentik ist ein Client-Secret üblich.</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>Zusätzliche Berechtigungsbereiche; "openid" ist immer dabei, ohne Angabe auch profile und email.</summary>
    public List<string> Scopes { get; set; } = [];

    public string UserNameClaim { get; set; } = "preferred_username";
    public string DisplayNameClaim { get; set; } = "name";

    /// <summary>
    /// Anspruch mit den Gruppen oder Rollen des Benutzers. authentik: "groups" (Gruppennamen).
    /// Entra: am besten "roles" (App-Rollen); "groups" liefert Objekt-IDs und ist bei sehr vielen Gruppen unvollständig.
    /// </summary>
    public string GroupsClaim { get; set; } = "groups";

    /// <summary>Werte des Gruppen-Anspruchs je ODM-Rolle. Ohne passenden Wert wird die Anmeldung abgelehnt.</summary>
    public List<string> AdminValues { get; set; } = [];

    public List<string> OperatorValues { get; set; } = [];
    public List<string> ReadOnlyValues { get; set; } = [];

    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName;

    public IReadOnlyList<string> EffectiveScopes =>
        (Scopes.Count == 0 ? DefaultScopes : Scopes.Prepend("openid"))
        .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct(StringComparer.Ordinal).ToList();

    public string MetadataAddress => Authority.TrimEnd('/') + "/.well-known/openid-configuration";

    public string? Problem()
    {
        if (!OidcOptions.IsValidId(Id)) return "Id may only contain a-z, 0-9 and hyphen (at most 32 characters).";
        if (!OidcOptions.IsAllowedUrl(Authority)) return "Authority must be an https address (http only for localhost).";
        if (string.IsNullOrWhiteSpace(ClientId)) return "ClientId fehlt.";
        if (string.IsNullOrWhiteSpace(GroupsClaim)) return "GroupsClaim fehlt.";
        if (string.IsNullOrWhiteSpace(UserNameClaim)) return "UserNameClaim fehlt.";
        if (AdminValues.Count + OperatorValues.Count + ReadOnlyValues.Count == 0)
        {
            return "At least one value in AdminValues, OperatorValues or ReadOnlyValues is required.";
        }

        return null;
    }
}

public static class OidcRoleMapper
{
    /// <summary>Höchste Rolle, für die einer der Werte konfiguriert ist; null = keine (Anmeldung abgelehnt).</summary>
    public static UserRole? Map(IEnumerable<string> values, OidcProviderOptions provider)
    {
        var set = values.Select(v => v.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool Any(IEnumerable<string> configured) => configured.Any(c => !string.IsNullOrWhiteSpace(c) && set.Contains(c.Trim()));

        if (Any(provider.AdminValues)) return UserRole.Admin;
        if (Any(provider.OperatorValues)) return UserRole.Operator;
        if (Any(provider.ReadOnlyValues)) return UserRole.ReadOnly;
        return null;
    }
}
