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

/// <summary>Protocol between agent and server (versioned under /api/agent/v1).</summary>
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
                    errors["token"] = ["Token is missing or invalid."];
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
                HttpContext http, ClaimsPrincipal principal, HeartbeatRequest request, AgentService service,
                AgentCertificateAuthority authority, ClientCertificateProvider certificates, IOptions<AgentsOptions> options, CancellationToken ct) =>
            {
                // Host name is optional (older agents); if given, the same rules apply as at registration.
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
                    // Without a certificate, "Required" mode delivers neither jobs nor results, only the request for one.
                    return Results.Ok(new HeartbeatResponse([], options.Value.HeartbeatSeconds, null, CertificateRequested: true));
                }

                var response = await service.HeartbeatAsync(agentId, deviceId, request, ct);
                return Results.Ok(response with { CertificateRequested = CertificateWanted(http, principal, authority, certificates, options.Value.Mtls) });
            })
            .RequireAuthorization(Policies.Agent);

        // Request or renew a certificate. Without a previous certificate the secret is enough (once), afterwards only with a certificate.
        group.MapPost("/certificate", async (
                HttpContext http, ClaimsPrincipal principal, CertificateRequestDto request, AgentService service,
                AgentCertificateAuthority authority, ClientCertificateProvider certificates, CancellationToken ct) =>
            {
                if (!authority.Enabled)
                {
                    return Results.Problem("Certificates are not enabled on this server.", statusCode: 404);
                }

                if (!certificates.CanPresentCertificate(http))
                {
                    // A certificate that cannot be presented over this connection would lock the device out.
                    return Results.Problem("Certificates are only issued over a direct TLS connection to ODM or through a trusted proxy that forwards them.", statusCode: 400);
                }

                if (string.IsNullOrWhiteSpace(request.Csr) || request.Csr.Length > 4000)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["csr"] = ["Invalid."] });
                }

                var agentId = Guid.Parse(principal.FindFirstValue("agent_id")!);
                try
                {
                    var response = await service.IssueCertificateAsync(agentId, request.Csr, ct);
                    return response is null ? Results.Unauthorized() : Results.Ok(response);
                }
                catch (CryptographicException)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["csr"] = ["Invalid certificate request."] });
                }
            })
            .RequireAuthorization(Policies.Agent);
    }

    /// <summary>Should the agent request a (new) certificate? If it has none or it expires soon.</summary>
    private static bool CertificateWanted(HttpContext http, ClaimsPrincipal principal, AgentCertificateAuthority authority, ClientCertificateProvider certificates, AgentMtlsOptions options)
    {
        if (!authority.Enabled || !certificates.CanPresentCertificate(http))
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
        if (string.IsNullOrWhiteSpace(hostname) || hostname.Length > 255) errors["hostname"] = ["Invalid."];
        if (osFamily is null || !OsFamilies.Contains(osFamily, StringComparer.OrdinalIgnoreCase)) errors["osFamily"] = ["Windows or Linux."];
        if (string.IsNullOrWhiteSpace(osName) || osName.Length > 128) errors["osName"] = ["Invalid."];
        if (osVersion is null || osVersion.Length > 64) errors["osVersion"] = ["Invalid."];
        if (agentVersion is null || agentVersion.Length > 32) errors["agentVersion"] = ["Invalid."];
        return errors;
    }
}
