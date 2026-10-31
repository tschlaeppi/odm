using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Odm.Modules.Agents.Application;
using Odm.Modules.Agents.Endpoints;
using Odm.Modules.Agents.Infrastructure;
using Odm.SharedKernel;
using Odm.SharedKernel.Contracts;
using Odm.SharedKernel.Persistence;

namespace Odm.Modules.Agents;

public sealed class AgentsModule : IModule
{
    public string Name => "Agents";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AgentsOptions>(configuration.GetSection(AgentsOptions.SectionName));

        services.AddDbContext<AgentsDbContext>((sp, o) =>
            o.UseOdmDatabase(sp.GetRequiredService<IOptions<DatabaseOptions>>().Value, AgentsDbContext.Schema));

        services.AddSingleton<AgentPackageCatalog>();
        services.AddSingleton<AgentCertificateAuthority>();
        services.AddScoped<AgentService>();
        services.AddScoped<IJobQueue, JobQueue>();

        // Additional authentication scheme for agents; the default (JWT for users) stays unchanged.
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, AgentAuthenticationHandler>(AgentAuthenticationHandler.SchemeName, _ => { });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Agent, p => p
                .AddAuthenticationSchemes(AgentAuthenticationHandler.SchemeName)
                .RequireClaim("device_id"));

        services.AddRateLimiter(limiter =>
        {
            limiter.AddPolicy("agent-register", ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));

            limiter.AddPolicy(InstallEndpoints.RateLimitPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 120,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        AgentEndpoints.Map(endpoints);
        InstallEndpoints.Map(endpoints);
        AdminEndpoints.Map(endpoints);
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentsDbContext>();
        await db.MigrateOdmAsync(ct);
    }
}
