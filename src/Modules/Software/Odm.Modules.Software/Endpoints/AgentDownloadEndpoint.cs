using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Odm.Modules.Software.Application;
using Odm.Modules.Software.Domain;
using Odm.Modules.Software.Infrastructure;
using Odm.SharedKernel;

namespace Odm.Modules.Software.Endpoints;

/// <summary>
/// Paket-Download für Agents. Der Server reicht die Datei aus dem Speicher durch, damit Agents
/// nur den ODM-Server erreichen müssen (kein direkter Zugriff auf S3, keine Zugangsdaten auf dem Gerät).
/// </summary>
internal static class AgentDownloadEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/agent/v1/packages/{versionId:guid}/download", async (
                Guid versionId, ClaimsPrincipal principal, SoftwareDbContext db, IPackageStorage storage, CancellationToken ct) =>
            {
                var deviceId = Guid.Parse(principal.FindFirstValue("device_id")!);

                // Nur Geräte, für die gerade ein Installationsauftrag mit genau dieser Version läuft.
                var allowed = await db.Installations.AnyAsync(
                    i => i.DeviceId == deviceId && i.JobVersionId == versionId && i.Status == InstallStatus.Running, ct);
                if (!allowed)
                {
                    return Results.NotFound();
                }

                var version = await db.Versions.AsNoTracking().SingleOrDefaultAsync(v => v.Id == versionId, ct);
                if (version is null)
                {
                    return Results.NotFound();
                }

                var stream = await storage.OpenReadAsync(version.ObjectKey, ct);
                return Results.Stream(stream, "application/zip");
            })
            .WithTags("Agent")
            .RequireAuthorization(Policies.Agent);
    }
}
