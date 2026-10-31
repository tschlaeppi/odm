using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Odm.Modules.Identity.Domain;
using Odm.Modules.Identity.Infrastructure;
using Odm.Modules.Identity.Infrastructure.Oidc;

namespace Odm.Tests;

public class OidcTests
{
    private const string Issuer = "https://auth.example.org/application/o/odm/";
    private const string ClientId = "client-1";
    private const string Nonce = "nonce-123";

    private static readonly RSA SigningKey = RSA.Create(2048);

    private static OidcProviderOptions Provider() => new()
    {
        Id = "authentik",
        DisplayName = "authentik",
        Authority = Issuer,
        ClientId = ClientId,
        AdminValues = ["odm-admins"],
        OperatorValues = ["odm-operators"]
    };

    private static OpenIdConnectConfiguration Discovery(RSA? key = null, string issuer = Issuer)
    {
        var config = new OpenIdConnectConfiguration { Issuer = issuer };
        config.SigningKeys.Add(new RsaSecurityKey(key ?? SigningKey) { KeyId = "k1" });
        return config;
    }

    private static string IdToken(
        string nonce = Nonce, string audience = ClientId, string issuer = Issuer, TimeSpan? expiresIn = null,
        RSA? signWith = null, Dictionary<string, object>? extra = null, string? algorithm = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = "user-42",
            ["nonce"] = nonce,
            ["preferred_username"] = "Anna.Muster",
            ["name"] = "Anna Muster",
            ["groups"] = new[] { "odm-operators", "staff" }
        };
        foreach (var (key, value) in extra ?? new Dictionary<string, object>())
        {
            claims[key] = value;
        }

        var delta = expiresIn ?? TimeSpan.FromMinutes(5);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            NotBefore = DateTime.UtcNow + (delta < TimeSpan.Zero ? delta - TimeSpan.FromMinutes(10) : TimeSpan.Zero),
            Expires = DateTime.UtcNow + delta,
            Claims = claims,
            SigningCredentials = algorithm == SecurityAlgorithms.HmacSha256
                ? new SigningCredentials(new SymmetricSecurityKey(new byte[32]), SecurityAlgorithms.HmacSha256)
                : new SigningCredentials(new RsaSecurityKey(signWith ?? SigningKey) { KeyId = "k1" }, SecurityAlgorithms.RsaSha256)
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    // ---- PKCE ----

    [Fact]
    public void Pkce_challenge_matches_the_rfc_7636_example() =>
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            Pkce.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));

    [Fact]
    public void Random_values_are_url_safe_unique_and_long_enough()
    {
        var a = Pkce.NewVerifier();
        var b = Pkce.NewVerifier();

        Assert.NotEqual(a, b);
        Assert.InRange(a.Length, 43, 128); // RFC 7636
        Assert.Matches("^[A-Za-z0-9_-]+$", a);
        Assert.Matches("^[A-Za-z0-9_-]+$", Pkce.NewToken());
    }

    // ---- Sign-in state in the cookie ----

    private static OidcStateProtector Protector(string secret = "a-long-test-secret-with-at-least-32-chars!") =>
        new(Microsoft.Extensions.Options.Options.Create(new JwtOptions { Secret = secret }));

    [Fact]
    public async Task State_round_trips()
    {
        var state = new OidcLoginState("authentik", "s", "n", "v");
        var restored = await Protector().UnprotectAsync(Protector().Protect(state));

        Assert.Equal(state, restored);
    }

    [Fact]
    public async Task Tampered_foreign_or_empty_state_is_rejected()
    {
        var protector = Protector();
        var cookie = protector.Protect(new OidcLoginState("authentik", "s", "n", "v"));

        var parts = cookie.Split('.');
        parts[1] = (parts[1][0] == 'A' ? 'B' : 'A') + parts[1][1..]; // content altered, signature stays
        Assert.Null(await protector.UnprotectAsync(string.Join('.', parts)));
        Assert.Null(await Protector("another-secret-another-secret-123456").UnprotectAsync(cookie));
        Assert.Null(await protector.UnprotectAsync(""));
        Assert.Null(await protector.UnprotectAsync(null));
        Assert.Null(await protector.UnprotectAsync("not.a.jwt"));
    }

    [Fact]
    public async Task State_cookie_is_not_a_valid_api_token()
    {
        // Same secret, but a different derived key: the cookie must never pass as an API token.
        const string secret = "a-long-test-secret-with-at-least-32-chars!";
        var cookie = Protector(secret).Protect(new OidcLoginState("authentik", "s", "n", "v"));

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(cookie, new TokenValidationParameters
        {
            ValidIssuer = "open-device-manager",
            ValidAudience = "open-device-manager",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))
        });

        Assert.False(result.IsValid);
    }

    // ---- ID-Token ----

    [Fact]
    public async Task Valid_token_yields_identity_with_groups()
    {
        var identity = await OidcIdTokenValidator.ValidateAsync(IdToken(), Discovery(), Provider(), Nonce);

        Assert.NotNull(identity);
        Assert.Equal("user-42", identity!.Subject);
        Assert.Equal("anna.muster", identity.UserName);
        Assert.Equal("Anna Muster", identity.DisplayName);
        Assert.Equal(Arr("odm-operators", "staff"), identity.GroupValues.ToArray());
        Assert.Equal(UserRole.Operator, OidcRoleMapper.Map(identity.GroupValues, Provider()));
    }

    [Fact]
    public async Task Token_with_wrong_nonce_audience_issuer_or_key_is_rejected()
    {
        var discovery = Discovery();
        var provider = Provider();

        Assert.Null(await OidcIdTokenValidator.ValidateAsync(IdToken(nonce: "other"), discovery, provider, Nonce));
        Assert.Null(await OidcIdTokenValidator.ValidateAsync(IdToken(audience: "someone-else"), discovery, provider, Nonce));
        Assert.Null(await OidcIdTokenValidator.ValidateAsync(IdToken(issuer: "https://evil.example.org/"), discovery, provider, Nonce));
        Assert.Null(await OidcIdTokenValidator.ValidateAsync(IdToken(signWith: RSA.Create(2048)), discovery, provider, Nonce));
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var token = IdToken(expiresIn: TimeSpan.FromMinutes(-10));
        Assert.Null(await OidcIdTokenValidator.ValidateAsync(token, Discovery(), Provider(), Nonce));
    }

    [Fact]
    public async Task Symmetric_signature_is_never_accepted()
    {
        var token = IdToken(algorithm: SecurityAlgorithms.HmacSha256);
        Assert.Null(await OidcIdTokenValidator.ValidateAsync(token, Discovery(), Provider(), Nonce));
    }

    [Fact]
    public async Task Missing_subject_is_rejected()
    {
        var token = IdToken(extra: new() { ["sub"] = "" });
        Assert.Null(await OidcIdTokenValidator.ValidateAsync(token, Discovery(), Provider(), Nonce));
    }

    [Fact]
    public async Task User_name_falls_back_to_email_then_subject()
    {
        var provider = Provider();
        provider.UserNameClaim = "upn";

        var withEmail = await OidcIdTokenValidator.ValidateAsync(
            IdToken(extra: new() { ["email"] = "Anna@Example.org" }), Discovery(), provider, Nonce);
        var withoutEmail = await OidcIdTokenValidator.ValidateAsync(IdToken(), Discovery(), provider, Nonce);

        Assert.Equal("anna@example.org", withEmail!.UserName);
        Assert.Equal("user-42", withoutEmail!.UserName);
    }

    [Fact]
    public async Task Single_string_claim_counts_as_one_group()
    {
        var provider = Provider();
        provider.GroupsClaim = "roles";

        var identity = await OidcIdTokenValidator.ValidateAsync(
            IdToken(extra: new() { ["roles"] = "odm-admins" }), Discovery(), provider, Nonce);

        Assert.Equal(UserRole.Admin, OidcRoleMapper.Map(identity!.GroupValues, provider));
    }

    // ---- Rollen ----

    [Fact]
    public void Roles_follow_priority_and_ignore_case()
    {
        var provider = Provider();
        provider.ReadOnlyValues = ["11111111-AAAA-bbbb-cccc-222222222222"];

        Assert.Equal(UserRole.Admin, OidcRoleMapper.Map(["odm-operators", "ODM-Admins"], provider));
        Assert.Equal(UserRole.Operator, OidcRoleMapper.Map(["odm-operators"], provider));
        Assert.Equal(UserRole.ReadOnly, OidcRoleMapper.Map(["11111111-aaaa-BBBB-cccc-222222222222"], provider));
        Assert.Null(OidcRoleMapper.Map(["staff"], provider));
        Assert.Null(OidcRoleMapper.Map([], provider));
    }

    [Fact]
    public void Empty_configured_values_never_match()
    {
        var provider = Provider();
        provider.AdminValues = [""];

        Assert.Null(OidcRoleMapper.Map([""], new OidcProviderOptions { AdminValues = [""] }));
        Assert.Equal(UserRole.Operator, OidcRoleMapper.Map(["odm-operators", ""], provider));
    }

    // ---- Konfiguration ----

    private static OidcOptions Config() => new() { PublicBaseUrl = "https://odm.example.org", Providers = [Provider()] };

    [Fact]
    public void Config_without_providers_is_fine()
    {
        Assert.Null(new OidcOptions().Problem());
    }

    [Fact]
    public void Valid_config_has_no_problem_and_builds_the_redirect_uri()
    {
        var config = Config();

        Assert.Null(config.Problem());
        Assert.Equal("https://odm.example.org/api/auth/oidc/callback", config.RedirectUri);
        Assert.True(config.SecureCookies);

        config.PublicBaseUrl = "https://odm.example.org/";
        Assert.Null(config.Problem());
        Assert.Equal("https://odm.example.org/api/auth/oidc/callback", config.RedirectUri);
    }

    [Fact]
    public void Insecure_or_odd_urls_are_rejected()
    {
        var http = Config();
        http.PublicBaseUrl = "http://odm.example.org";
        Assert.NotNull(http.Problem());

        var withPath = Config();
        withPath.PublicBaseUrl = "https://odm.example.org/app";
        Assert.NotNull(withPath.Problem());

        var localhost = Config();
        localhost.PublicBaseUrl = "http://localhost:5173";
        Assert.Null(localhost.Problem());
        Assert.False(localhost.SecureCookies);

        var insecureAuthority = Config();
        insecureAuthority.Providers[0].Authority = "http://auth.example.org/";
        Assert.NotNull(insecureAuthority.Problem());
    }

    [Fact]
    public void Provider_problems_are_reported()
    {
        var duplicate = Config();
        duplicate.Providers.Add(Provider());
        Assert.NotNull(duplicate.Problem());

        var badId = Config();
        badId.Providers[0].Id = "Not Valid!";
        Assert.NotNull(badId.Problem());

        var noClient = Config();
        noClient.Providers[0].ClientId = "";
        Assert.NotNull(noClient.Problem());

        var noRoles = Config();
        noRoles.Providers[0].AdminValues = [];
        noRoles.Providers[0].OperatorValues = [];
        Assert.NotNull(noRoles.Problem());
    }

    [Fact]
    public void Scopes_always_include_openid()
    {
        var provider = Provider();
        Assert.Equal(Arr("openid", "profile", "email"), provider.EffectiveScopes.ToArray());

        provider.Scopes = ["profile", "openid", "api://odm/access"];
        Assert.Equal(Arr("openid", "profile", "api://odm/access"), provider.EffectiveScopes.ToArray());

        provider.Scopes = ["email"];
        Assert.Equal(Arr("openid", "email"), provider.EffectiveScopes.ToArray());
    }

    [Fact]
    public void Metadata_address_is_built_from_the_authority()
    {
        Assert.Equal("https://auth.example.org/application/o/odm/.well-known/openid-configuration",
            Provider().MetadataAddress);

        var entra = Provider();
        entra.Authority = "https://login.microsoftonline.com/tenant-id/v2.0";
        Assert.Equal("https://login.microsoftonline.com/tenant-id/v2.0/.well-known/openid-configuration",
            entra.MetadataAddress);
    }
}
