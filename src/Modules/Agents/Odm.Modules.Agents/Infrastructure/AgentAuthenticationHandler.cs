using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Odm.Modules.Agents.Domain;

namespace Odm.Modules.Agents.Infrastructure;

/// <summary>
/// Authentisiert Agents über den Header "Authorization: Agent {agentId}.{secret}".
/// Eigenes Schema, damit Agent-Zugangsdaten nie für Benutzer-Endpunkte gelten (und umgekehrt).
/// </summary>
public sealed class AgentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Agent";

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
            return AuthenticateResult.Fail("Ungültige Agent-Zugangsdaten.");
        }

        var secret = parameter[(separator + 1)..];
        var db = Context.RequestServices.GetRequiredService<AgentsDbContext>();
        var credential = await db.Credentials.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == agentId && c.RevokedAt == null, Context.RequestAborted);

        if (credential is null || !CryptographicOperations.FixedTimeEquals(Secrets.Hash(secret), credential.SecretHash))
        {
            return AuthenticateResult.Fail("Ungültige Agent-Zugangsdaten.");
        }

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim("agent_id", credential.Id.ToString()),
                new Claim("device_id", credential.DeviceId.ToString())
            },
            SchemeName);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
