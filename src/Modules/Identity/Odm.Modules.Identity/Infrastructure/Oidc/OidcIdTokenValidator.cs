using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Odm.Modules.Identity.Infrastructure.Oidc;

/// <summary>Der geprüfte Benutzer aus einem ID-Token.</summary>
public sealed record OidcIdentity(string Subject, string UserName, string DisplayName, IReadOnlyList<string> GroupValues);

/// <summary>
/// Prüft ein ID-Token: Signatur (Schlüssel des Anbieters), Aussteller, Empfänger (unsere Client-ID), Ablauf und nonce.
/// Getrennt vom Netzwerk, damit es mit eigenen Schlüsseln testbar ist.
/// </summary>
public static class OidcIdTokenValidator
{
    private static readonly string[] AsymmetricAlgorithms =
    [
        SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512,
        SecurityAlgorithms.RsaSsaPssSha256, SecurityAlgorithms.RsaSsaPssSha384, SecurityAlgorithms.RsaSsaPssSha512,
        SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.EcdsaSha384, SecurityAlgorithms.EcdsaSha512
    ];

    public static async Task<OidcIdentity?> ValidateAsync(
        string idToken, OpenIdConnectConfiguration discovery, OidcProviderOptions provider, string expectedNonce)
    {
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidIssuer = discovery.Issuer,
            ValidAudience = provider.ClientId,
            IssuerSigningKeys = discovery.SigningKeys,
            // Nur asymmetrische Verfahren: ein Token mit "HS256" oder "none" wird nie akzeptiert.
            ValidAlgorithms = AsymmetricAlgorithms,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        });

        if (!result.IsValid)
        {
            return null;
        }

        var claims = result.ClaimsIdentity.Claims.ToList();
        string? First(string type) => claims.FirstOrDefault(c => c.Type == type)?.Value;

        var nonce = First("nonce");
        if (nonce is null || !FixedTimeEquals(nonce, expectedNonce))
        {
            return null;
        }

        if (First("sub") is not { Length: > 0 } subject)
        {
            return null;
        }

        var userName = (First(provider.UserNameClaim) ?? First("email") ?? subject).Trim().ToLowerInvariant();
        if (userName.Length is 0 or > 256)
        {
            return null;
        }

        var display = First(provider.DisplayNameClaim)?.Trim();
        var groups = claims.Where(c => c.Type == provider.GroupsClaim).Select(c => c.Value).ToList();

        return new OidcIdentity(subject, userName, string.IsNullOrEmpty(display) ? userName : display, groups);
    }

    /// <summary>Zeitkonstanter Vergleich, damit state/nonce nicht über die Antwortzeit erraten werden können.</summary>
    public static bool FixedTimeEquals(string a, string b)
    {
        var x = Encoding.UTF8.GetBytes(a);
        var y = Encoding.UTF8.GetBytes(b);
        return x.Length == y.Length && CryptographicOperations.FixedTimeEquals(x, y);
    }
}
