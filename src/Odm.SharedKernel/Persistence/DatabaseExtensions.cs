using Microsoft.EntityFrameworkCore;

namespace Odm.SharedKernel.Persistence;

public static class DatabaseExtensions
{
    /// <summary>
    /// Konfiguriert den DB-Provider zentral. Jedes Modul besitzt ein eigenes Schema
    /// und eine eigene Migrations-Historie.
    /// </summary>
    public static DbContextOptionsBuilder UseOdmDatabase(
        this DbContextOptionsBuilder builder, DatabaseOptions options, string schema)
    {
        switch (options.Provider)
        {
            case DatabaseProvider.Postgres:
                builder.UseNpgsql(
                    options.ConnectionString,
                    npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", schema));
                break;
            case DatabaseProvider.SqlServer:
                throw new NotSupportedException("SQL-Server-Unterstützung ist für eine spätere Etappe geplant.");
            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Provider, null);
        }

        return builder;
    }

    /// <summary>Optionen für 'dotnet ef' (IDesignTimeDbContextFactory). Verbindung via ODM_DB.</summary>
    public static DbContextOptions<TContext> CreateDesignTimeOptions<TContext>(string schema)
        where TContext : DbContext
    {
        var options = new DatabaseOptions
        {
            Provider = DatabaseProvider.Postgres,
            ConnectionString = Environment.GetEnvironmentVariable("ODM_DB")
                               ?? "Host=localhost;Database=odm;Username=odm;Password=odm"
        };

        var builder = new DbContextOptionsBuilder<TContext>();
        builder.UseOdmDatabase(options, schema);
        return builder.Options;
    }
}
