using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Odm.Modules.Identity.Application;
using Odm.Modules.Identity.Infrastructure.Ldap;

namespace Odm.Modules.Identity.Infrastructure.Oidc;

public sealed record OidcStart(string AuthorizationUrl, string StateCookie);

/// <summary>Ergebnis der Rückkehr vom Anbieter: Anmeldung oder Fehlerart ("denied" = keine Rolle, "failed" = sonst).</summary>
public sealed record OidcResult(LoginResponse? Response, string? Error);

/// <summary>
/// Authorization-Code-Ablauf mit PKCE. Der Server holt die Tokens selbst über den Token-Endpoint ab
/// (Rückkanal, nicht über den Browser) und stellt danach ein ODM-Token aus.
/// </summary>
public sealed class OidcService(
    IOptions<OidcOptions> options,
    OidcStateProtector protector,
    ITokenService tokens,
    IHttpClientFactory httpFactory,
    ILogger<OidcService> logger)
{
    public const string HttpClientName = "oidc";

    private readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> _managers = new();

    public IReadOnlyList<(string Id, string DisplayName)> Providers =>
        options.Value.Providers.Select(p => (p.Id, p.Label)).ToList();

    public async Task<OidcStart?> StartAsync(string providerId, CancellationToken ct)
    {
        var provider = Find(providerId);
        if (provider is null)
        {
            return null;
        }

        var discovery = await ConfigurationAsync(provider, ct);
        var state = new OidcLoginState(provider.Id, Pkce.NewToken(), Pkce.NewToken(), Pkce.NewVerifier());

        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = provider.ClientId,
            ["redirect_uri"] = options.Value.RedirectUri,
            ["scope"] = string.Join(' ', provider.EffectiveScopes),
            ["state"] = state.State,
            ["nonce"] = state.Nonce,
            ["code_challenge"] = Pkce.Challenge(state.Verifier),
            ["code_challenge_method"] = "S256"
        };

        var url = discovery.AuthorizationEndpoint + (discovery.AuthorizationEndpoint.Contains('?') ? "&" : "?") +
                  string.Join('&', query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        return new OidcStart(url, protector.Protect(state));
    }

    public async Task<OidcResult> CompleteAsync(string? code, string? stateParam, string? stateCookie, CancellationToken ct)
    {
        try
        {
            var saved = await protector.UnprotectAsync(stateCookie);
            if (saved is null || string.IsNullOrEmpty(stateParam) ||
                !OidcIdTokenValidator.FixedTimeEquals(saved.State, stateParam) || string.IsNullOrEmpty(code))
            {
                logger.LogWarning("OIDC-Rückkehr mit ungültigem oder fehlendem Anmeldezustand abgelehnt.");
                return new OidcResult(null, "failed");
            }

            var provider = Find(saved.Provider);
            if (provider is null)
            {
                return new OidcResult(null, "failed");
            }

            var discovery = await ConfigurationAsync(provider, ct);
            var idToken = await ExchangeCodeAsync(provider, discovery, code, saved.Verifier, ct);
            if (idToken is null)
            {
                return new OidcResult(null, "failed");
            }

            var identity = await OidcIdTokenValidator.ValidateAsync(idToken, discovery, provider, saved.Nonce);
            if (identity is null)
            {
                // Mögliche Schlüsselrotation beim Anbieter: Schlüssel neu laden und einmal erneut prüfen.
                _managers[provider.Id].RequestRefresh();
                discovery = await ConfigurationAsync(provider, ct);
                identity = await OidcIdTokenValidator.ValidateAsync(idToken, discovery, provider, saved.Nonce);
            }

            if (identity is null)
            {
                logger.LogWarning("ID-Token von '{Provider}' ist ungültig.", provider.Id);
                return new OidcResult(null, "failed");
            }

            if (OidcRoleMapper.Map(identity.GroupValues, provider) is not { } role)
            {
                logger.LogInformation("OIDC-Benutzer '{UserName}' ({Provider}) hat keine ODM-Rolle; abgelehnt.", identity.UserName, provider.Id);
                return new OidcResult(null, "denied");
            }

            var user = new AuthenticatedUser(
                ExternalUserIds.Create($"oidc:{provider.Id}", identity.Subject), identity.UserName, identity.DisplayName, role);
            var (token, expiresAt) = tokens.Create(user);
            return new OidcResult(new LoginResponse(token, expiresAt, user.ToDto()), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "OIDC-Anmeldung fehlgeschlagen.");
            return new OidcResult(null, "failed");
        }
    }

    private OidcProviderOptions? Find(string id) =>
        options.Value.Providers.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));

    private Task<OpenIdConnectConfiguration> ConfigurationAsync(OidcProviderOptions provider, CancellationToken ct)
    {
        var manager = _managers.GetOrAdd(provider.Id, _ => new ConfigurationManager<OpenIdConnectConfiguration>(
            provider.MetadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(httpFactory.CreateClient(HttpClientName)) { RequireHttps = !IsLoopback(provider.Authority) }));
        return manager.GetConfigurationAsync(ct);
    }

    private async Task<string?> ExchangeCodeAsync(
        OidcProviderOptions provider, OpenIdConnectConfiguration discovery, string code, string verifier, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = options.Value.RedirectUri,
            ["client_id"] = provider.ClientId,
            ["code_verifier"] = verifier
        };

        if (!string.IsNullOrEmpty(provider.ClientSecret))
        {
            form["client_secret"] = provider.ClientSecret;
        }

        var client = httpFactory.CreateClient(HttpClientName);
        using var response = await client.PostAsync(discovery.TokenEndpoint, new FormUrlEncodedContent(form), ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            // Nur den Fehlercode des Anbieters protokollieren, nie den ganzen Inhalt.
            logger.LogWarning("Token-Endpoint von '{Provider}' antwortete {Status} ({Error}).",
                provider.Id, (int)response.StatusCode, ErrorCode(body));
            return null;
        }

        using var json = JsonDocument.Parse(body);
        return json.RootElement.TryGetProperty("id_token", out var idToken) && idToken.ValueKind == JsonValueKind.String
            ? idToken.GetString()
            : null;
    }

    private static string ErrorCode(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString() ?? "-"
                : "-";
        }
        catch (JsonException)
        {
            return "-";
        }
    }

    private static bool IsLoopback(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsLoopback;
}
