using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Odm.Modules.Audit.Application;
using Odm.Modules.Audit.Endpoints;
using Odm.Modules.Audit.Infrastructure;
using Odm.SharedKernel;
using Odm.SharedKernel.Contracts;
using Odm.SharedKernel.Persistence;

namespace Odm.Modules.Audit;

/// <summary>Audit log: who changed what and when. Other modules write through <see cref="IAuditLog"/>.</summary>
public sealed class AuditModule : IModule
{
    public string Name => "Audit";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuditOptions>(configuration.GetSection(AuditOptions.SectionName));
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);

        services.AddDbContext<AuditDbContext>((sp, o) =>
            o.UseOdmDatabase(sp.GetRequiredService<IOptions<DatabaseOptions>>().Value, AuditDbContext.Schema));
        services.AddScoped<IAuditLog, AuditLog>();

        services.AddTransient<IStartupFilter, AuditStartupFilter>();
        services.AddHostedService<AuditRetentionService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => AuditEndpoints.Map(endpoints);

    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        await db.MigrateOdmAsync(ct);
    }
}
