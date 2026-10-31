using System.Net.Mime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Odm.Modules.Agents.Application;

namespace Odm.Modules.Agents.Endpoints;

/// <summary>Anonyme Endpunkte für die Agent-Installation: Skripte, Paketliste und Downloads.</summary>
internal static class InstallEndpoints
{
    public const string RateLimitPolicy = "agent-install";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/agent").WithTags("Agent-Installation").AllowAnonymous().RequireRateLimiting(RateLimitPolicy);

        group.MapGet("/install.sh", (HttpContext http, AgentPackageCatalog catalog, IOptions<AgentsOptions> options) =>
            Script(http, catalog, options.Value, "install.sh"));

        group.MapGet("/install.ps1", (HttpContext http, AgentPackageCatalog catalog, IOptions<AgentsOptions> options) =>
            Script(http, catalog, options.Value, "install.ps1"));

        group.MapGet("/packages", (HttpContext http, AgentPackageCatalog catalog, IOptions<AgentsOptions> options) =>
        {
            var baseUrl = BaseUrl(http, options.Value);
            if (baseUrl is null)
            {
                return Results.Problem("The server address is invalid.", statusCode: 400);
            }

            var packages = catalog.List()
                .Select(p => new AgentPackageDto(p.Platform, p.Version, p.FileName, p.Size, p.Sha256, $"{baseUrl}/api/agent/download/{p.Platform}"))
                .ToList();
            return Results.Ok(new AgentPackagesResponse(baseUrl, packages));
        });

        group.MapGet("/download/{platform}", (string platform, string? version, AgentPackageCatalog catalog) =>
        {
            // Die Version wird nur als Suchschlüssel verwendet; der Dateiname stammt immer aus dem Paketverzeichnis.
            var package = string.IsNullOrEmpty(version) ? catalog.Find(platform) : catalog.Find(platform, version);
            return package is null
                ? Results.NotFound()
                : Results.File(package.FullPath, MediaTypeNames.Application.Octet, package.FileName, enableRangeProcessing: true);
        });
    }

    private static IResult Script(HttpContext http, AgentPackageCatalog catalog, AgentsOptions options, string template)
    {
        var baseUrl = BaseUrl(http, options);
        if (baseUrl is null)
        {
            return Results.Problem("The server address is invalid. Configure Agents:PublicBaseUrl.", statusCode: 400);
        }

        var packages = catalog.List();
        if (packages.Count == 0)
        {
            return Results.Problem("No agent packages are available on this server.", statusCode: 404);
        }

        http.Response.Headers.CacheControl = "no-store";
        return Results.Text(InstallScriptBuilder.Build(template, baseUrl, packages), "text/plain; charset=utf-8");
    }

    private static string? BaseUrl(HttpContext http, AgentsOptions options)
    {
        var configured = options.PublicBaseUrl;
        var request = http.Request;
        return InstallScriptBuilder.NormalizeBaseUrl(
            string.IsNullOrWhiteSpace(configured) ? $"{request.Scheme}://{request.Host}{request.PathBase}" : configured);
    }
}

public sealed record AgentPackageDto(string Platform, string Version, string FileName, long Size, string Sha256, string Url);

public sealed record AgentPackagesResponse(string ServerUrl, IReadOnlyList<AgentPackageDto> Packages);
