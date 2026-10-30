using Microsoft.Extensions.Options;
using Odm.Agent;
using Odm.Agent.Jobs;
using Odm.Agent.Os;

var builder = Host.CreateApplicationBuilder(args);

// Vom Installationsskript geschriebene Konfiguration (ServerUrl, einmaliges Token) im geschützten Zustandsverzeichnis.
// Sie hat Vorrang vor appsettings.json und Umgebungsvariablen; fehlt sie, gilt die übliche Konfiguration.
// Ist sie für den aktuellen Benutzer nicht lesbar (Agent von Hand ohne Administratorrechte gestartet), wird sie übersprungen.
if (File.Exists(AgentStateStore.ConfigFile))
{
    try
    {
        using (File.OpenRead(AgentStateStore.ConfigFile)) { }
        builder.Configuration.AddJsonFile(AgentStateStore.ConfigFile, optional: false, reloadOnChange: false);
    }
    catch (UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"Warnung: {AgentStateStore.ConfigFile} ist nicht lesbar - als Administrator/root starten.");
    }
}

// Läuft als Windows-Dienst bzw. systemd-Dienst; beim Start aus der Konsole sind beide Aufrufe wirkungslos.
builder.Services.AddWindowsService(o => o.ServiceName = "OpenDeviceManagerAgent");
builder.Services.AddSystemd();

builder.Services.AddOptions<AgentOptions>()
    .Bind(builder.Configuration.GetSection(AgentOptions.SectionName))
    .Validate(o => o.Validate() is null, "Ungültige Agent-Konfiguration (ServerUrl).")
    .ValidateOnStart();

builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
    return new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    {
        BaseAddress = new Uri(options.ServerUrl.TrimEnd('/') + "/"),
        Timeout = TimeSpan.FromSeconds(30)
    };
});

builder.Services.AddSingleton<ServerClient>();
builder.Services.AddSingleton<AgentStateStore>();
builder.Services.AddSingleton<SystemInfoProvider>();
builder.Services.AddSingleton<IJobHandler, PingJobHandler>();
builder.Services.AddSingleton<IJobHandler, InstallPsadtJobHandler>();
builder.Services.AddSingleton<IJobHandler, InstallRepoJobHandler>();
builder.Services.AddSingleton<WindowsUpdater>();
builder.Services.AddSingleton<LinuxUpdater>();
builder.Services.AddSingleton<IJobHandler, OsUpdateJobHandler>();
builder.Services.AddSingleton<JobExecutor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<JobExecutor>());
builder.Services.AddHostedService<AgentWorker>();

await builder.Build().RunAsync();
