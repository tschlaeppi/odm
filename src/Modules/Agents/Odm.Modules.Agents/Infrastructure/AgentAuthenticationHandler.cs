using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Odm.Modules.Agents.Application;
using Odm.Modules.Agents.Domain;

namespace Odm.Modules.Agents.Infrastructure;

/// <summary>
/// Authenticates agents through the header "Authorization: Agent {agentId}.{secret}".
/// Own scheme so that agent credentials never apply to user endpoints (and vice versa).
/// </summary>
public sealed class AgentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Agent";

    public const string MtlsClaim = "mtls";
    public const string CertExpiryClaim = "cert_expiry";
    public const string MtlsOff = "off";
    public const string MtlsUnenrolled = "unenrolled";
    public const string MtlsVerified = "verified";

    private static bool IsBootstrapPath(PathString path) =>
        path.Value is { } value &&
        (value.EndsWith("/agent/v1/heartbeat", StringComparison.OrdinalIgnoreCase) ||
         value.EndsWith("/agent/v1/certificate", StringComparison.OrdinalIgnoreCase));

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var header))
        {
            return AuthenticateResult.NoResult();
        }

        var value = header.ToString();
        if (!value.StartsWith(SchemeName + " ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var parameter = value[(SchemeName.Length + 1)..].Trim();
        var separator = parameter.IndexOf('.');
        if (separator <= 0 || !Guid.TryParse(parameter[..separator], out var agentId))
        {
            return AuthenticateResult.Fail("Invalid agent credentials.");
        }

        var secret = parameter[(separator + 1)..];
        var db = Context.RequestServices.GetRequiredService<AgentsDbContext>();
        var credential = await db.Credentials.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == agentId && c.RevokedAt == null, Context.RequestAborted);

        if (credential is null || !CryptographicOperations.FixedTimeEquals(Secrets.Hash(secret), credential.SecretHash))
        {
            return AuthenticateResult.Fail("Invalid agent credentials.");
        }

        var claims = new List<Claim>
        {
            new("agent_id", credential.Id.ToString()),
            new("device_id", credential.DeviceId.ToString())
        };

        var authority = Context.RequestServices.GetRequiredService<AgentCertificateAuthority>();
        if (!authority.Enabled)
        {
            claims.Add(new Claim(MtlsClaim, MtlsOff));
        }
        else if (credential.CertThumbprint is null)
        {
            // No certificate issued yet: only the secret counts. In "Required" mode the agent may then
            // call only heartbeat and certificate request (no access to jobs, packages, secrets).
            if (authority.Mode == AgentMtlsMode.Required && !IsBootstrapPath(Request.Path))
            {
                return AuthenticateResult.Fail("Client certificate required.");
            }

            claims.Add(new Claim(MtlsClaim, MtlsUnenrolled));
        }
        else
        {
            // Whoever has a certificate must always present it; a stolen secret alone is no longer enough.
            var certificate = await Context.Connection.GetClientCertificateAsync(Context.RequestAborted);
            if (certificate is null ||
                !authority.IsValid(certificate, credential.Id, credential.CertThumbprint, DateTimeOffset.UtcNow))
            {
                return AuthenticateResult.Fail("Valid client certificate required.");
            }

            claims.Add(new Claim(MtlsClaim, MtlsVerified));
            if (credential.CertNotAfter is { } notAfter)
            {
                claims.Add(new Claim(CertExpiryClaim, notAfter.ToUnixTimeSeconds().ToString()));
            }
        }

        var identity = new ClaimsIdentity(claims, SchemeName);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
