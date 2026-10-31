using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Odm.Modules.OsUpdates.Application;
using Odm.Modules.OsUpdates.Endpoints;
using Odm.Modules.OsUpdates.Infrastructure;
using Odm.SharedKernel;
using Odm.SharedKernel.Contracts;
using Odm.SharedKernel.Persistence;

namespace Odm.Modules.OsUpdates;

public sealed class OsUpdatesModule : IModule
{
    public string Name => "OsUpdates";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<OsUpdatesDbContext>((sp, o) =>
            o.UseOdmDatabase(sp.GetRequiredService<IOptions<DatabaseOptions>>().Value, OsUpdatesDbContext.Schema));

        services.AddScoped<UpdateSchedulingService>();
        services.AddScoped<IJobResultListener, UpdateRunListener>();
        services.AddHostedService<UpdateScheduler>();
        services.AddHostedService<UpdateRunRetentionService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        PolicyEndpoints.Map(endpoints);
        WindowEndpoints.Map(endpoints);
        AssignmentEndpoints.Map(endpoints);
        RunEndpoints.Map(endpoints);
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OsUpdatesDbContext>().MigrateOdmAsync(ct);
    }
}
