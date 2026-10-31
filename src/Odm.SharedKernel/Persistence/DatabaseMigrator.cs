using Microsoft.EntityFrameworkCore;

namespace Odm.SharedKernel.Persistence;

public static class DatabaseMigrator
{
    /// <summary>
    /// Applies the migrations. If they are missing entirely, startup aborts with a clear message instead of
    /// continuing with an empty database and failing only at the first query.
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
