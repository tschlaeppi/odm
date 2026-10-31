using Microsoft.EntityFrameworkCore;

namespace Odm.SharedKernel.Persistence;

public static class DatabaseExtensions
{
    /// <summary>
    /// Assembly mit den SQL-Server-Migrationen. Migrationen sind providerspezifisch (Spaltentypen, Annotationen);
    /// Postgres behält seine Migrationen in den Modulen, SQL Server hat ein eigenes Projekt.
    /// </summary>
    public const string SqlServerMigrationsAssembly = "Odm.Migrations.SqlServer";

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
                builder.UseSqlServer(
                    options.ConnectionString,
                    sql =>
                    {
                        sql.MigrationsHistoryTable("__ef_migrations", schema);
                        sql.MigrationsAssembly(SqlServerMigrationsAssembly);
                    });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Provider, null);
        }

        return builder;
    }

    /// <summary>
    /// Optionen für 'dotnet ef' (IDesignTimeDbContextFactory). Verbindung via ODM_DB, Provider via ODM_DB_PROVIDER
    /// (Standard Postgres), sofern der Aufrufer ihn nicht vorgibt.
    /// </summary>
    public static DbContextOptions<TContext> CreateDesignTimeOptions<TContext>(
        string schema, DatabaseProvider? provider = null)
        where TContext : DbContext
    {
        var effective = provider ??
                        (Enum.TryParse<DatabaseProvider>(Environment.GetEnvironmentVariable("ODM_DB_PROVIDER"), true, out var fromEnv)
                            ? fromEnv
                            : DatabaseProvider.Postgres);

        var connection = Environment.GetEnvironmentVariable("ODM_DB");
        if (string.IsNullOrWhiteSpace(connection))
        {
            connection = effective == DatabaseProvider.Postgres
                ? "Host=localhost;Database=odm;Username=odm;Password=odm"
                : throw new InvalidOperationException("Set the environment variable ODM_DB (connection string) for SQL Server.");
        }

        var builder = new DbContextOptionsBuilder<TContext>();
        builder.UseOdmDatabase(new DatabaseOptions { Provider = effective, ConnectionString = connection }, schema);
        return builder.Options;
    }
}
