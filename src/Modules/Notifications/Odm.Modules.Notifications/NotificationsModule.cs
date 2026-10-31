using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Odm.Modules.Notifications.Application;
using Odm.Modules.Notifications.Endpoints;
using Odm.Modules.Notifications.Infrastructure;
using Odm.SharedKernel;
using Odm.SharedKernel.Persistence;

namespace Odm.Modules.Notifications;

/// <summary>E-mail and webhook alerts, sent once per event. Other modules contribute through <c>IAlertSource</c>.</summary>
public sealed class NotificationsModule : IModule
{
    public string Name => "Notifications";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<NotificationOptions>(configuration.GetSection(NotificationOptions.SectionName));
        services.AddDbContext<NotificationsDbContext>((sp, o) =>
            o.UseOdmDatabase(sp.GetRequiredService<IOptions<DatabaseOptions>>().Value, NotificationsDbContext.Schema));

        services.AddHttpClient(WebhookChannel.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddScoped<IAlertChannel, EmailChannel>();
        services.AddScoped<IAlertChannel, WebhookChannel>();
        services.AddScoped<NotificationService>();
        services.AddHostedService<NotificationWorker>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => NotificationEndpoints.Map(endpoints);

    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        await db.MigrateOdmAsync(ct);
    }
}
