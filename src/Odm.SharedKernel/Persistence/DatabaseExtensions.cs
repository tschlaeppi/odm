using Microsoft.EntityFrameworkCore;

namespace Odm.SharedKernel.Persistence;

public static class DatabaseExtensions
{
    /// <summary>
    /// Assembly with the SQL Server migrations. Migrations are provider-specific (column types, annotations);
    /// PostgreSQL keeps its migrations in the modules, SQL Server has its own project.
    /// </summary>
    public const string SqlServerMigrationsAssembly = "Odm.Migrations.SqlServer";

    /// <summary>
    /// Configures the DB provider centrally. Each module has its own schema
    /// and its own migration history.
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
    /// Options for 'dotnet ef' (IDesignTimeDbContextFactory). Connection via ODM_DB, provider via ODM_DB_PROVIDER
    /// (default PostgreSQL), unless the caller specifies it.
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
