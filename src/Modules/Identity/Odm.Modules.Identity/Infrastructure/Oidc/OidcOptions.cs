using System.Text.RegularExpressions;
using Odm.Modules.Identity.Domain;

namespace Odm.Modules.Identity.Infrastructure.Oidc;

/// <summary>
/// Sign-in through OpenID Connect (Microsoft Entra ID, authentik, Keycloak, Google and others).
/// Several providers at once are possible; each appears as its own button on the sign-in page.
/// </summary>
public sealed partial class OidcOptions
{
    public const string SectionName = "Identity:Oidc";

    [GeneratedRegex("^[a-z0-9-]{1,32}$")]
    private static partial Regex IdPattern();

    /// <summary>Public address of ODM, e.g. https://odm.example.org. The redirect URI is derived from it.</summary>
    public string PublicBaseUrl { get; set; } = "";

    public List<OidcProviderOptions> Providers { get; set; } = [];

    public string RedirectUri => PublicBaseUrl.TrimEnd('/') + "/api/auth/oidc/callback";

    public bool SecureCookies => PublicBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Describes the first problem of the configuration or returns null. Without a provider everything is fine.</summary>
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

    /// <summary>Allows https; http only for localhost (development).</summary>
    public static bool IsAllowedUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));

    internal static bool IsValidId(string id) => IdPattern().IsMatch(id);
}

public sealed class OidcProviderOptions
{
    private static readonly string[] DefaultScopes = ["openid", "profile", "email"];

    /// <summary>Short name for the address, only a–z, 0–9 and hyphen (e.g. "entra").</summary>
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Issuer address. Entra: https://login.microsoftonline.com/&lt;directory-id&gt;/v2.0 (not "common");
    /// authentik: https://auth.example.org/application/o/&lt;slug&gt;/.
    /// </summary>
    public string Authority { get; set; } = "";

    public string ClientId { get; set; } = "";

    /// <summary>Empty = public client (PKCE only). A client secret is common for Entra and authentik.</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>Additional scopes; "openid" is always included, without a value also profile and email.</summary>
    public List<string> Scopes { get; set; } = [];

    public string UserNameClaim { get; set; } = "preferred_username";
    public string DisplayNameClaim { get; set; } = "name";

    /// <summary>
    /// Claim with the user's groups or roles. authentik: "groups" (group names).
    /// Entra: preferably "roles" (app roles); "groups" returns object IDs and is incomplete with very many groups.
    /// </summary>
    public string GroupsClaim { get; set; } = "groups";

    /// <summary>Values of the group claim per ODM role. Without a matching value the sign-in is rejected.</summary>
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
        if (string.IsNullOrWhiteSpace(ClientId)) return "ClientId is missing.";
        if (string.IsNullOrWhiteSpace(GroupsClaim)) return "GroupsClaim is missing.";
        if (string.IsNullOrWhiteSpace(UserNameClaim)) return "UserNameClaim is missing.";
        if (AdminValues.Count + OperatorValues.Count + ReadOnlyValues.Count == 0)
        {
            return "At least one value in AdminValues, OperatorValues or ReadOnlyValues is required.";
        }

        return null;
    }
}

public static class OidcRoleMapper
{
    /// <summary>Highest role for which one of the values is configured; null = none (sign-in rejected).</summary>
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
