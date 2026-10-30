using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Odm.Modules.Identity.Infrastructure.Oidc;

/// <summary>PKCE (RFC 7636) und Zufallswerte für state und nonce.</summary>
public static class Pkce
{
    public static string NewVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>Zufallswert für state und nonce.</summary>
    public static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Was der Browser zwischen Start und Rückkehr der Anmeldung mitführt.</summary>
public sealed record OidcLoginState(string Provider, string State, string Nonce, string Verifier);

/// <summary>
/// Schützt den Anmeldezustand in einem kurzlebigen, signierten Cookie. So braucht der Server keinen Zwischenspeicher
/// und die Anmeldung funktioniert auch mit mehreren Instanzen. Der Schlüssel wird vom JWT-Secret abgeleitet und ist
/// nicht der Schlüssel der API-Tokens; ein Cookie lässt sich also nicht als API-Token verwenden.
/// </summary>
public sealed class OidcStateProtector(IOptions<JwtOptions> jwt)
{
    private const string Issuer = "odm-oidc-state";
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(10);

    private SymmetricSecurityKey Key() =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes("odm-oidc-state\n" + jwt.Value.Secret)));

    public string Protect(OidcLoginState state, TimeSpan? lifetime = null) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Issuer,
            Expires = DateTime.UtcNow.Add(lifetime ?? DefaultLifetime),
            Claims = new Dictionary<string, object>
            {
                ["provider"] = state.Provider,
                ["state"] = state.State,
                ["nonce"] = state.Nonce,
                ["verifier"] = state.Verifier
            },
            SigningCredentials = new SigningCredentials(Key(), SecurityAlgorithms.HmacSha256)
        });

    public async Task<OidcLoginState?> UnprotectAsync(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(value, new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = Issuer,
            IssuerSigningKey = Key(),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        });

        if (!result.IsValid)
        {
            return null;
        }

        string? Get(string name) => result.Claims.TryGetValue(name, out var v) ? v as string : null;

        return Get("provider") is { Length: > 0 } provider &&
               Get("state") is { Length: > 0 } state &&
               Get("nonce") is { Length: > 0 } nonce &&
               Get("verifier") is { Length: > 0 } verifier
            ? new OidcLoginState(provider, state, nonce, verifier)
            : null;
    }
}
