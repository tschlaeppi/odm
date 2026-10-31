using Microsoft.EntityFrameworkCore;

namespace Odm.SharedKernel.Persistence;

public static class DatabaseMigrator
{
    /// <summary>
    /// Wendet die Migrationen an. Fehlen sie ganz, bricht der Start mit einer klaren Meldung ab, statt mit einer
    /// leeren Datenbank weiterzulaufen und erst bei der ersten Abfrage zu scheitern.
    /// </summary>
    public static async Task MigrateOdmAsync(this DbContext db, CancellationToken ct = default)
    {
        if (!db.Database.GetMigrations().Any())
        {
            var name = db.GetType().Name;
            throw new InvalidOperationException(
                db.Database.IsSqlServer()
                    ? $"There are no SQL Server migrations for {name}. Create them once with scripts/ef-sqlserver.ps1 or " +
                      "scripts/ef-sqlserver.sh (name e.g. Initial) and commit the folder src/Odm.Migrations.SqlServer."
                    : $"There are no migrations for {name}. Create them with 'dotnet ef migrations add' (see docs/development.md).");
        }

        await db.Database.MigrateAsync(ct);
    }
}
