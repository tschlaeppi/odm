using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Odm.Modules.Software.Application;
using Odm.Modules.Software.Endpoints;
using Odm.Modules.Software.Infrastructure;
using Odm.SharedKernel;
using Odm.SharedKernel.Contracts;
using Odm.SharedKernel.Persistence;

namespace Odm.Modules.Software;

public sealed class SoftwareModule : IModule
{
    private long _maxPackageBytes = 4096L * 1024 * 1024;

    public string Name => "Software";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SoftwareOptions>(configuration.GetSection(SoftwareOptions.SectionName));
        _maxPackageBytes = Math.Max(1, configuration.GetValue<int?>("Software:MaxPackageMegabytes") ?? 4096) * 1024L * 1024;

        var formLimit = _maxPackageBytes + 1024 * 1024;
        services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = formLimit);

        services.AddOptions<S3Options>()
            .Bind(configuration.GetSection(S3Options.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Bucket) &&
                           !string.IsNullOrWhiteSpace(o.AccessKey) &&
                           !string.IsNullOrWhiteSpace(o.SecretKey),
                "Storage:S3 (Bucket, AccessKey, SecretKey) ist nicht konfiguriert.")
            .ValidateOnStart();

        services.AddDbContext<SoftwareDbContext>((sp, o) =>
            o.UseOdmDatabase(sp.GetRequiredService<IOptions<DatabaseOptions>>().Value, SoftwareDbContext.Schema));

        services.AddOptions<SecretsOptions>()
            .Bind(configuration.GetSection(SecretsOptions.SectionName))
            .Validate(o => SecretsOptions.IsValid(o.MasterKey),
                "Secrets:MasterKey muss ein Base64-codierter 256-Bit-Schlüssel sein (z. B. 'openssl rand -base64 32').")
            .ValidateOnStart();

        services.AddSingleton<SecretProtector>();
        services.AddSingleton<IPackageStorage, S3PackageStorage>();
        services.AddSingleton<DeploymentTrigger>();
        services.AddScoped<DeploymentService>();
        services.AddScoped<IJobResultListener, DeploymentJobListener>();
        services.AddHostedService<DeploymentReconciler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        PackageEndpoints.Map(endpoints, _maxPackageBytes);
        AssignmentEndpoints.Map(endpoints);
        ProfileEndpoints.Map(endpoints);
        AgentDownloadEndpoint.Map(endpoints);
        SecretEndpoints.Map(endpoints);
        StatsEndpoint.Map(endpoints);
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SoftwareDbContext>().MigrateOdmAsync(ct);

        // Ist der Speicher beim Start nicht erreichbar, soll ODM trotzdem starten; der Upload versucht es erneut.
        try
        {
            await services.GetRequiredService<IPackageStorage>().EnsureBucketAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            services.GetRequiredService<ILogger<SoftwareModule>>()
                .LogWarning(ex, "S3 storage is not reachable at startup.");
        }
    }
}
