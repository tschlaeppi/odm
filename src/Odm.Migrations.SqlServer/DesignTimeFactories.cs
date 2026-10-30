using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Odm.Modules.Agents.Infrastructure;
using Odm.Modules.Devices.Infrastructure;
using Odm.Modules.Identity.Infrastructure;
using Odm.Modules.OsUpdates.Infrastructure;
using Odm.Modules.Software.Infrastructure;
using Odm.SharedKernel.Persistence;

namespace Odm.Migrations.SqlServer;

// Je Modul eine Fabrik, die 'dotnet ef' für SQL Server verwendet (die Fabriken in den Modulen gelten für Postgres).
// Die Verbindung kommt aus ODM_DB; zum Erzeugen von Migrationen wird keine Datenbank gebraucht.

internal sealed class IdentityFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args) =>
        new(DatabaseExtensions.CreateDesignTimeOptions<IdentityDbContext>(IdentityDbContext.Schema, DatabaseProvider.SqlServer));
}

internal sealed class DevicesFactory : IDesignTimeDbContextFactory<DevicesDbContext>
{
    public DevicesDbContext CreateDbContext(string[] args) =>
        new(DatabaseExtensions.CreateDesignTimeOptions<DevicesDbContext>(DevicesDbContext.Schema, DatabaseProvider.SqlServer));
}

internal sealed class AgentsFactory : IDesignTimeDbContextFactory<AgentsDbContext>
{
    public AgentsDbContext CreateDbContext(string[] args) =>
        new(DatabaseExtensions.CreateDesignTimeOptions<AgentsDbContext>(AgentsDbContext.Schema, DatabaseProvider.SqlServer));
}

internal sealed class SoftwareFactory : IDesignTimeDbContextFactory<SoftwareDbContext>
{
    public SoftwareDbContext CreateDbContext(string[] args) =>
        new(DatabaseExtensions.CreateDesignTimeOptions<SoftwareDbContext>(SoftwareDbContext.Schema, DatabaseProvider.SqlServer));
}

internal sealed class OsUpdatesFactory : IDesignTimeDbContextFactory<OsUpdatesDbContext>
{
    public OsUpdatesDbContext CreateDbContext(string[] args) =>
        new(DatabaseExtensions.CreateDesignTimeOptions<OsUpdatesDbContext>(OsUpdatesDbContext.Schema, DatabaseProvider.SqlServer));
}
