using Microsoft.AspNetCore.Server.Kestrel.Https;
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

// mTLS für Agents: HTTPS-Endpunkte fordern ein Client-Zertifikat an, verlangen es aber nicht (Browser ohne Zertifikat
// funktionieren weiter). Ob ein Zertifikat gültig und das richtige ist, prüft die Agent-Authentisierung (Agents-Modul).
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
