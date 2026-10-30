using System.Security.Claims;
using System.Text.RegularExpressions;
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
/// Verwaltung der Secrets (nur Namen sichtbar, Werte werden nie zurückgegeben) und Ausgabe an Agents.
/// </summary>
internal static class SecretEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/software/secrets").WithTags("Software").RequireAuthorization();

        // Namen sind für Operatoren sichtbar (zum Verweisen in Argumenten), Werte für niemanden.
        group.MapGet("/", async (SoftwareDbContext db, CancellationToken ct) =>
            Results.Ok((await db.Secrets.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct))
                .Select(s => new SecretDto(s.Name, s.UpdatedAt))));

        group.MapPut("/{name}", async (string name, SetSecretRequest request, SoftwareDbContext db, SecretProtector protector, CancellationToken ct) =>
            {
                if (!Regex.IsMatch(name, SecretReferences.NamePattern))
                {
                    return Invalid("name", "Name: 1-64 Zeichen (Buchstaben, Ziffern, . _ -).");
                }

                if (string.IsNullOrEmpty(request.Value) || request.Value.Length > 4000)
                {
                    return Invalid("value", "Wert ist erforderlich (höchstens 4000 Zeichen).");
                }

                var ciphertext = protector.Protect(name, request.Value);
                var existing = await db.Secrets.SingleOrDefaultAsync(s => s.Name == name, ct);
                if (existing is null)
                {
                    db.Secrets.Add(SoftwareSecret.Create(name, ciphertext));
                }
                else
                {
                    existing.Replace(ciphertext);
                }

                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            })
            .RequireAuthorization(Policies.Admin);

        group.MapDelete("/{name}", async (string name, SoftwareDbContext db, CancellationToken ct) =>
            {
                // Ein Secret, auf das noch Pakete verweisen, darf nicht gelöscht werden.
                var packages = await db.Packages.AsNoTracking().ToListAsync(ct);
                var snapshots = await db.Versions.AsNoTracking()
                    .Where(v => v.SpecSnapshot != null)
                    .Select(v => v.SpecSnapshot)
                    .ToListAsync(ct);
                var used = packages.Any(p => SecretReferences.Extract(p.Spec.Arguments).Contains(name))
                           || snapshots.Any(s => SecretReferences.Extract(PackageSpec.Parse(s).Arguments).Contains(name));
                if (used)
                {
                    return Results.Conflict();
                }

                var deleted = await db.Secrets.Where(s => s.Name == name).ExecuteDeleteAsync(ct);
                return deleted == 0 ? Results.NotFound() : Results.NoContent();
            })
            .RequireAuthorization(Policies.Admin);

        // Agent: Werte der Secrets, die der gerade laufende Auftrag in seinen Argumenten verwendet.
        endpoints.MapPost("/agent/v1/secrets/resolve", async (
                ResolveSecretsRequest request, ClaimsPrincipal principal, SoftwareDbContext db,
                SecretProtector protector, CancellationToken ct) =>
            {
                var deviceId = Guid.Parse(principal.FindFirstValue("device_id")!);

                // Nur für einen laufenden Installationsauftrag dieses Geräts, und nur die dort referenzierten Secrets.
                var installation = await db.Installations.AsNoTracking().SingleOrDefaultAsync(
                    i => i.JobId == request.JobId && i.DeviceId == deviceId && i.Status == InstallStatus.Running, ct);
                if (installation is null)
                {
                    return Results.NotFound();
                }

                var package = await db.Packages.AsNoTracking().SingleOrDefaultAsync(p => p.Id == installation.PackageId, ct);
                if (package is null)
                {
                    return Results.NotFound();
                }

                string? arguments;
                if (package.Kind.IsRepo())
                {
                    var snapshot = await db.Versions.AsNoTracking()
                        .Where(v => v.Id == installation.JobVersionId)
                        .Select(v => v.SpecSnapshot)
                        .SingleOrDefaultAsync(ct);
                    arguments = PackageSpec.Parse(snapshot).Arguments;
                }
                else
                {
                    arguments = package.Spec.Arguments;
                }

                var names = SecretReferences.Extract(arguments);
                var rows = await db.Secrets.AsNoTracking().Where(s => names.Contains(s.Name)).ToListAsync(ct);
                var values = rows.ToDictionary(s => s.Name, s => protector.Unprotect(s.Name, s.Ciphertext));
                return Results.Ok(new ResolveSecretsResponse(values));
            })
            .WithTags("Agent")
            .RequireAuthorization(Policies.Agent);
    }

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
