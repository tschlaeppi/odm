using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
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
                ClaimsPrincipal principal, HeartbeatRequest request, AgentService service, CancellationToken ct) =>
            {
                // Rechnername ist optional (ältere Agents); wenn angegeben, gelten dieselben Regeln wie bei der Registrierung.
                var errors = Validate(request.Hostname ?? "-", request.OsFamily, request.OsName, request.OsVersion, request.AgentVersion);
                if (errors.Count > 0)
                {
                    return Results.ValidationProblem(errors);
                }

                var agentId = Guid.Parse(principal.FindFirstValue("agent_id")!);
                var deviceId = Guid.Parse(principal.FindFirstValue("device_id")!);
                return Results.Ok(await service.HeartbeatAsync(agentId, deviceId, request, ct));
            })
            .RequireAuthorization(Policies.Agent);
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
