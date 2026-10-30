using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using Odm.Modules.Agents.Infrastructure;
using Odm.Modules.Agents.Application;
using Odm.SharedKernel;

namespace Odm.Modules.Agents.Endpoints;

/// <summary>Protokoll zwischen Agent und Server (versioniert unter /api/agent/v1).</summary>
internal static class AgentEndpoints
{
    private static readonly string[] OsFamilies = ["Windows", "Linux"];

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/agent/v1").WithTags("Agent");

        group.MapPost("/register", async (RegisterRequest request, AgentService service, CancellationToken ct) =>
            {
                var errors = Validate(request.Hostname, request.OsFamily, request.OsName, request.OsVersion, request.AgentVersion);
                if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 200)
                {
                    errors["token"] = ["Token fehlt oder ist ungültig."];
                }

                if (errors.Count > 0)
                {
                    return Results.ValidationProblem(errors);
                }

                var response = await service.RegisterAsync(request, ct);
                return response is null ? Results.Unauthorized() : Results.Ok(response);
            })
            .AllowAnonymous()
            .RequireRateLimiting("agent-register");

        group.MapPost("/heartbeat", async (
                ClaimsPrincipal principal, HeartbeatRequest request, AgentService service,
                AgentCertificateAuthority authority, IOptions<AgentsOptions> options, CancellationToken ct) =>
            {
                // Rechnername ist optional (ältere Agents); wenn angegeben, gelten dieselben Regeln wie bei der Registrierung.
                var errors = Validate(request.Hostname ?? "-", request.OsFamily, request.OsName, request.OsVersion, request.AgentVersion);
                if (errors.Count > 0)
                {
                    return Results.ValidationProblem(errors);
                }

                var agentId = Guid.Parse(principal.FindFirstValue("agent_id")!);
                var deviceId = Guid.Parse(principal.FindFirstValue("device_id")!);

                var mtls = principal.FindFirstValue(AgentAuthenticationHandler.MtlsClaim);
                if (mtls == AgentAuthenticationHandler.MtlsUnenrolled && authority.Mode == AgentMtlsMode.Required)
                {
                    // Ohne Zertifikat gibt es im Modus "Erforderlich" weder Aufträge noch Ergebnisse, nur die Aufforderung dazu.
                    return Results.Ok(new HeartbeatResponse([], options.Value.HeartbeatSeconds, null, CertificateRequested: true));
                }

                var response = await service.HeartbeatAsync(agentId, deviceId, request, ct);
                return Results.Ok(response with { CertificateRequested = CertificateWanted(principal, authority, options.Value.Mtls) });
            })
            .RequireAuthorization(Policies.Agent);

        // Zertifikat anfordern bzw. erneuern. Ohne bisheriges Zertifikat genügt das Secret (einmalig), danach nur noch mit Zertifikat.
        group.MapPost("/certificate", async (
                ClaimsPrincipal principal, CertificateRequestDto request, AgentService service,
                AgentCertificateAuthority authority, CancellationToken ct) =>
            {
                if (!authority.Enabled)
                {
                    return Results.Problem("Zertifikate sind auf diesem Server nicht aktiviert.", statusCode: 404);
                }

                if (string.IsNullOrWhiteSpace(request.Csr) || request.Csr.Length > 4000)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["csr"] = ["Ungültig."] });
                }

                var agentId = Guid.Parse(principal.FindFirstValue("agent_id")!);
                try
                {
                    var response = await service.IssueCertificateAsync(agentId, request.Csr, ct);
                    return response is null ? Results.Unauthorized() : Results.Ok(response);
                }
                catch (CryptographicException)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["csr"] = ["Ungültige Zertifikatsanforderung."] });
                }
            })
            .RequireAuthorization(Policies.Agent);
    }

    /// <summary>Soll der Agent ein (neues) Zertifikat anfordern? Wenn er keines hat oder es bald abläuft.</summary>
    private static bool CertificateWanted(ClaimsPrincipal principal, AgentCertificateAuthority authority, AgentMtlsOptions options)
    {
        if (!authority.Enabled)
        {
            return false;
        }

        return principal.FindFirstValue(AgentAuthenticationHandler.MtlsClaim) switch
        {
            AgentAuthenticationHandler.MtlsUnenrolled => true,
            AgentAuthenticationHandler.MtlsVerified when
                long.TryParse(principal.FindFirstValue(AgentAuthenticationHandler.CertExpiryClaim), out var expiry) =>
                DateTimeOffset.FromUnixTimeSeconds(expiry) - DateTimeOffset.UtcNow < TimeSpan.FromDays(Math.Max(1, options.RenewBeforeDays)),
            _ => false
        };
    }

    private static Dictionary<string, string[]> Validate(
        string? hostname, string? osFamily, string? osName, string? osVersion, string? agentVersion)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(hostname) || hostname.Length > 255) errors["hostname"] = ["Ungültig."];
        if (osFamily is null || !OsFamilies.Contains(osFamily, StringComparer.OrdinalIgnoreCase)) errors["osFamily"] = ["Windows oder Linux."];
        if (string.IsNullOrWhiteSpace(osName) || osName.Length > 128) errors["osName"] = ["Ungültig."];
        if (osVersion is null || osVersion.Length > 64) errors["osVersion"] = ["Ungültig."];
        if (agentVersion is null || agentVersion.Length > 32) errors["agentVersion"] = ["Ungültig."];
        return errors;
    }
}
