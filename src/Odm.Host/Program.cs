using Microsoft.AspNetCore.Server.Kestrel.Https;
using Odm.Modules.Agents;
using Odm.Modules.Devices;
using Odm.Modules.Identity;
using Odm.Modules.OsUpdates;
using Odm.Modules.Software;
using Odm.SharedKernel;
using Odm.SharedKernel.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Order = initialization order. New modules are registered here.
IModule[] modules =
[
    new IdentityModule(),
    new DevicesModule(),
    new AgentsModule(),
    new SoftwareModule(),
    new OsUpdatesModule()
];

builder.Services
    .AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), "Database:ConnectionString fehlt.")
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

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

foreach (var module in modules)
{
    module.ConfigureServices(builder.Services, builder.Configuration);
}

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseExceptionHandler();
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
