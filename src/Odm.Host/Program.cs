using Odm.Modules.Agents;
using Odm.Modules.Devices;
using Odm.Modules.Identity;
using Odm.Modules.OsUpdates;
using Odm.Modules.Software;
using Odm.SharedKernel;
using Odm.SharedKernel.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Reihenfolge = Initialisierungsreihenfolge. Neue Module werden hier registriert.
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
