using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Odm.SharedKernel.Persistence;

namespace Odm.Tests;

public class DatabaseProviderTests
{
    private sealed class DummyContext(DbContextOptions<DummyContext> options) : DbContext(options);

    private static RelationalOptionsExtension Relational(DbContextOptions options) =>
        options.Extensions.OfType<RelationalOptionsExtension>().Single();

    private static DbContextOptions<DummyContext> Build(DatabaseProvider provider, string connection)
    {
        var builder = new DbContextOptionsBuilder<DummyContext>();
        builder.UseOdmDatabase(new DatabaseOptions { Provider = provider, ConnectionString = connection }, "demo");
        return builder.Options;
    }

    [Fact]
    public void Postgres_keeps_migrations_in_the_module_assembly()
    {
        var relational = Relational(Build(DatabaseProvider.Postgres, "Host=localhost;Database=odm"));

        Assert.Null(relational.MigrationsAssembly);
        Assert.Equal("__ef_migrations", relational.MigrationsHistoryTableName);
        Assert.Equal("demo", relational.MigrationsHistoryTableSchema);
    }

    [Fact]
    public void Sql_server_uses_its_own_migrations_assembly_and_history_table()
    {
        var relational = Relational(Build(DatabaseProvider.SqlServer, "Server=localhost;Database=odm"));

        Assert.Equal(DatabaseExtensions.SqlServerMigrationsAssembly, relational.MigrationsAssembly);
        Assert.Equal("__ef_migrations", relational.MigrationsHistoryTableName);
        Assert.Equal("demo", relational.MigrationsHistoryTableSchema);
    }

    [Fact]
    public void Providers_use_different_extensions()
    {
        var postgres = Relational(Build(DatabaseProvider.Postgres, "Host=localhost")).GetType().Name;
        var sql = Relational(Build(DatabaseProvider.SqlServer, "Server=localhost")).GetType().Name;

        Assert.NotEqual(postgres, sql);
        Assert.Contains("Npgsql", postgres);
        Assert.Contains("SqlServer", sql);
    }

    [Fact]
    public void Migrations_assembly_name_matches_the_project()
    {
        Assert.Equal("Odm.Migrations.SqlServer", DatabaseExtensions.SqlServerMigrationsAssembly);
    }
}
