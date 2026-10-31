using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Odm.Modules.Software.Application;
using Odm.Modules.Software.Domain;
using Odm.Modules.Software.Infrastructure;
using Odm.SharedKernel;

namespace Odm.Modules.Software.Endpoints;

internal static partial class PackageEndpoints
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+\-]{0,63}$")]
    private static partial Regex VersionPattern();

    public static void Map(IEndpointRouteBuilder endpoints, long maxPackageBytes)
    {
        var group = endpoints.MapGroup("/software/packages").WithTags("Software").RequireAuthorization();

        group.MapGet("/", async (SoftwareDbContext db, CancellationToken ct) =>
        {
            var packages = await db.Packages.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);
            var versions = await db.Versions.AsNoTracking()
                .Select(v => new { v.PackageId, v.Version, v.UploadedAt })
                .ToListAsync(ct);

            var result = packages.Select(p =>
            {
                var own = versions.Where(v => v.PackageId == p.Id).OrderByDescending(v => v.UploadedAt).ToList();
                return new PackageDto(p.Id, p.Name, p.Description, p.Kind.ToString(), p.CreatedAt,
                    own.FirstOrDefault()?.Version, own.Count, ToDto(p.Spec));
            });
            return Results.Ok(result);
        });

        group.MapGet("/{id:guid}", async (Guid id, SoftwareDbContext db, CancellationToken ct) =>
        {
            var package = await db.Packages.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
            if (package is null)
            {
                return Results.NotFound();
            }

            var versions = await db.Versions.AsNoTracking()
                .Where(v => v.PackageId == id)
                .OrderByDescending(v => v.UploadedAt)
                .ToListAsync(ct);

            return Results.Ok(new PackageDetailDto(
                package.Id, package.Name, package.Description, package.Kind.ToString(), package.CreatedAt,
                ToDto(package.Spec),
                versions.Select(v => new VersionDto(v.Id, v.Version, v.SizeBytes, v.Sha256, v.Notes, v.UploadedAt)).ToList()));
        });

        group.MapPost("/", async (CreatePackageRequest request, SoftwareDbContext db, CancellationToken ct) =>
            {
                var name = request.Name?.Trim() ?? "";
                if (name.Length is 0 or > 128 || request.Description is { Length: > 1024 })
                {
                    return Invalid("name", "Name ist erforderlich (höchstens 128 Zeichen), Beschreibung höchstens 1024 Zeichen.");
                }

                if (!Enum.TryParse<PackageKind>(request.Kind ?? nameof(PackageKind.Psadt), true, out var kind) || !Enum.IsDefined(kind))
                {
                    return Invalid("kind", "Unbekannte Paketart.");
                }

                var secrets = await KnownSecretsAsync(db, ct);
                if (!PackageSpecValidator.TryValidate(kind, request.Spec, secrets, out var spec, out var errors))
                {
                    return Results.ValidationProblem(errors);
                }

                var lowered = name.ToLower();
                if (await db.Packages.AnyAsync(p => p.Name.ToLower() == lowered, ct))
                {
                    return Results.Conflict();
                }

                var package = SoftwarePackage.Create(name, request.Description, kind, spec);
                db.Packages.Add(package);

                // Repo-Pakete haben keine Datei: die erste Revision macht sie direkt zuweisbar.
                if (kind.IsRepo())
                {
                    db.Versions.Add(PackageVersion.CreateRevision(package.Id, RevisionLabel(spec, 1), spec));
                }

                await db.SaveChangesAsync(ct);
                return Results.Created($"/api/software/packages/{package.Id}",
                    new PackageDto(package.Id, package.Name, package.Description, package.Kind.ToString(), package.CreatedAt,
                        null, 0, ToDto(spec)));
            })
            .RequireAuthorization(Policies.Operator);

        // Beschreibung und Einstellungen ändern. Bei Repo-Paketen entsteht dabei eine neue Revision,
        // sodass alle zugewiesenen Geräte auf die neue Konfiguration abgeglichen werden.
        group.MapPut("/{id:guid}", async (
                Guid id, UpdatePackageRequest request, SoftwareDbContext db, DeploymentTrigger trigger, CancellationToken ct) =>
            {
                if (request.Description is { Length: > 1024 })
                {
                    return Invalid("description", "Höchstens 1024 Zeichen.");
                }

                var package = await db.Packages.SingleOrDefaultAsync(p => p.Id == id, ct);
                if (package is null)
                {
                    return Results.NotFound();
                }

                var newName = request.Name?.Trim();
                if (newName is not null && newName != package.Name)
                {
                    if (newName.Length is 0 or > 128)
                    {
                        return Invalid("name", "Name ist erforderlich (höchstens 128 Zeichen).");
                    }

                    var lowered = newName.ToLower();
                    if (await db.Packages.AnyAsync(p => p.Id != id && p.Name.ToLower() == lowered, ct))
                    {
                        return Results.Conflict();
                    }

                    package.Rename(newName);
                }

                var secrets = await KnownSecretsAsync(db, ct);
                if (!PackageSpecValidator.TryValidate(package.Kind, request.Spec, secrets, out var spec, out var errors))
                {
                    return Results.ValidationProblem(errors);
                }

                var changed = package.SpecJson != spec.ToJson();
                package.Update(request.Description, spec);

                if (changed && package.Kind.IsRepo())
                {
                    var count = await db.Versions.CountAsync(v => v.PackageId == id, ct);
                    db.Versions.Add(PackageVersion.CreateRevision(id, RevisionLabel(spec, count + 1), spec));
                }

                await db.SaveChangesAsync(ct);
                trigger.Request();
                return Results.NoContent();
            })
            .RequireAuthorization(Policies.Operator);

        // Paket samt Versionen, Zuweisungen und Installationseinträgen löschen. Bereits installierte Software bleibt auf den Geräten.
        group.MapDelete("/{id:guid}", async (
                Guid id, SoftwareDbContext db, IPackageStorage storage, ILogger<SoftwareModule> logger, CancellationToken ct) =>
            {
                var package = await db.Packages.SingleOrDefaultAsync(p => p.Id == id, ct);
                if (package is null)
                {
                    return Results.NotFound();
                }

                var keys = await db.Versions
                    .Where(v => v.PackageId == id && v.ObjectKey != null)
                    .Select(v => v.ObjectKey!)
                    .ToListAsync(ct);

                db.Packages.Remove(package);
                await db.SaveChangesAsync(ct);

                foreach (var key in keys)
                {
                    try
                    {
                        await storage.DeleteAsync(key, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogWarning(ex, "Object {Key} could not be deleted from storage.", key);
                    }
                }

                return Results.NoContent();
            })
            .RequireAuthorization(Policies.Operator);

        group.MapPost("/{id:guid}/versions", async (
                Guid id, HttpRequest http, SoftwareDbContext db, IPackageStorage storage,
                DeploymentTrigger trigger, CancellationToken ct) =>
            {
                // Kestrel begrenzt Anfragen standardmässig auf 30 MB; für Pakete ist das Limit konfigurierbar.
                var sizeFeature = http.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (sizeFeature is { IsReadOnly: false })
                {
                    sizeFeature.MaxRequestBodySize = maxPackageBytes + 1024 * 1024;
                }

                if (!http.HasFormContentType)
                {
                    return Invalid("file", "multipart/form-data erwartet.");
                }

                var form = await http.ReadFormAsync(ct);
                var version = form["version"].ToString().Trim();
                var notes = form["notes"].ToString();
                var file = form.Files["file"];

                var errors = new Dictionary<string, string[]>();
                if (!VersionPattern().IsMatch(version)) errors["version"] = ["Version: 1-64 Zeichen (Buchstaben, Ziffern, . _ + -)."];
                if (notes.Length > 1024) errors["notes"] = ["Höchstens 1024 Zeichen."];
                if (file is null || file.Length == 0) errors["file"] = ["ZIP-Datei fehlt."];
                else if (file.Length > maxPackageBytes) errors["file"] = ["Datei überschreitet die erlaubte Grösse."];
                if (errors.Count > 0)
                {
                    return Results.ValidationProblem(errors);
                }

                var package = await db.Packages.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
                if (package is null)
                {
                    return Results.NotFound();
                }

                if (package.Kind != PackageKind.Psadt)
                {
                    return Invalid("file", "Nur PSADT-Pakete haben Dateien; Repo-Pakete werden über ihre Einstellungen geändert.");
                }

                if (await db.Versions.AnyAsync(v => v.PackageId == id && v.Version == version, ct))
                {
                    return Results.Conflict();
                }

                // Erst lokal zwischenspeichern: Prüfsumme und ZIP-Prüfung brauchen die ganze Datei, S3 die Länge.
                var temp = Path.GetTempFileName();
                try
                {
                    await using (var target = File.Create(temp))
                    {
                        await file!.CopyToAsync(target, ct);
                    }

                    var zipError = PsadtZip.Validate(temp);
                    if (zipError is not null)
                    {
                        return Invalid("file", zipError);
                    }

                    string sha256;
                    await using (var stream = File.OpenRead(temp))
                    {
                        sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
                    }

                    var versionId = Guid.CreateVersion7();
                    var key = $"packages/{id:N}/{versionId:N}.zip";
                    await storage.PutFileAsync(key, temp, ct);

                    try
                    {
                        var entity = PackageVersion.Create(versionId, id, version, key, file.Length, sha256, notes);
                        db.Versions.Add(entity);
                        await db.SaveChangesAsync(ct);
                        trigger.Request(); // Zuweisungen mit "neueste Version" sollen sofort aktualisiert werden

                        return Results.Created($"/api/software/packages/{id}",
                            new VersionDto(entity.Id, entity.Version, entity.SizeBytes, entity.Sha256, entity.Notes, entity.UploadedAt));
                    }
                    catch
                    {
                        await storage.DeleteAsync(key, CancellationToken.None); // kein verwaistes Objekt hinterlassen
                        throw;
                    }
                }
                finally
                {
                    File.Delete(temp);
                }
            })
            .RequireAuthorization(Policies.Operator)
            // Bearer-Token statt Cookies: kein CSRF-Risiko, daher ist Antiforgery hier nicht nötig.
            .DisableAntiforgery();
    }

    internal static async Task<HashSet<string>> KnownSecretsAsync(SoftwareDbContext db, CancellationToken ct) =>
        (await db.Secrets.AsNoTracking().Select(s => s.Name).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);

    private static string RevisionLabel(PackageSpec spec, int number) => $"{spec.Version ?? "latest"} (r{number})";

    private static PackageSpecDto ToDto(PackageSpec s) =>
        new(s.Identifier, s.Version, s.Scope, s.Source, s.Remote, s.Channel, s.Classic, s.Arguments);

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
