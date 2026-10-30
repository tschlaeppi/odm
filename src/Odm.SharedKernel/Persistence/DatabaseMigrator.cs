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
                    ? $"Für {name} gibt es keine SQL-Server-Migrationen. Einmalig erzeugen: scripts/ef-sqlserver.ps1 bzw. " +
                      "scripts/ef-sqlserver.sh (Name z. B. Initial) und den Ordner src/Odm.Migrations.SqlServer einchecken."
                    : $"Für {name} gibt es keine Migrationen. Mit 'dotnet ef migrations add' erzeugen (siehe README).");
        }

        await db.Database.MigrateAsync(ct);
    }
}
