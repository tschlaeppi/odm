using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Odm.Modules.Agents;
using Odm.Modules.Audit;
using Odm.Modules.Agents.Infrastructure;
using Odm.Modules.Devices;
using Odm.Modules.Identity;
using Odm.Modules.Notifications;
using Odm.Modules.OsUpdates;
using Odm.Modules.Software;
using Odm.SharedKernel;
using Odm.SharedKernel.Net;
using Odm.SharedKernel.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Order = initialization order. New modules are registered here.
IModule[] modules =
[
    new IdentityModule(),
    new AuditModule(),
    new DevicesModule(),
    new AgentsModule(),
    new SoftwareModule(),
    new NotificationsModule(),
    new OsUpdatesModule()
];

builder.Services
    .AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), "Database:ConnectionString is missing.")
    .ValidateOnStart();

// mTLS for agents: HTTPS endpoints request a client certificate but do not require it (browsers without a certificate
// keep working). Whether a certificate is valid and the right one is checked by the agent authentication (Agents module).
var mtlsMode = builder.Configuration["Agents:Mtls:Mode"];
if (!string.IsNullOrWhiteSpace(mtlsMode) && !mtlsMode.Equals("Off", StringComparison.OrdinalIgnoreCase))
{
    builder.WebHost.ConfigureKestrel(kestrel => kestrel.ConfigureHttpsDefaults(https =>
    {
        https.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
        https.ClientCertificateValidation = (_, _, _) => true;
    }));
}

// Behind a reverse proxy (Proxy:ForwardedHeaders=true) client address and scheme come from X-Forwarded-For / -Proto.
// With Proxy:TrustedProxies (addresses or CIDR ranges) only those peers are believed; without it every peer is.
// Forwarding a client certificate (Agents:Mtls:ForwardedCertificateHeader) always requires TrustedProxies.
var forwardedHeaders = builder.Configuration.GetValue<bool>("Proxy:ForwardedHeaders");
if (forwardedHeaders)
{
    var trusted = TrustedProxies.FromConfiguration(builder.Configuration);
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var network in trusted.Networks)
        {
            options.KnownIPNetworks.Add(network);
        }
    });
}

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

foreach (var module in modules)
{
    module.ConfigureServices(builder.Services, builder.Configuration);
}

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseExceptionHandler();

// Remember the directly connected peer before forwarded headers replace the remote address: only trusted proxies
// may hand on a client certificate.
app.Use((context, next) =>
{
    context.Items[ClientCertificateProvider.PeerItemKey] = context.Connection.RemoteIpAddress;
    return next(context);
});

if (forwardedHeaders)
{
    app.UseForwardedHeaders();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");

var api = app.MapGroup("/api");
foreach (var module in modules)
{
    module.MapEndpoints(api);
}
api.MapFallback(() => Results.NotFound());

// SPA-Fallback (Production: Frontend liegt in wwwroot)
app.MapFallbackToFile("index.html");

foreach (var module in modules)
{
    await module.InitializeAsync(app.Services, app.Lifetime.ApplicationStopping);
}

app.Run();
