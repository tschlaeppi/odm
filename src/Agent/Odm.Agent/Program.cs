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
        Console.Error.WriteLine($"Warning: {AgentStateStore.ConfigFile} is not readable - run as administrator/root.");
    }
}

// Logdatei im Zustandsverzeichnis (Windows: C:\ProgramData\OpenDeviceManager\logs, Linux: /var/lib/odm-agent/logs),
// zusätzlich zu Konsole/journald bzw. Ereignisanzeige. Abschaltbar mit Agent:LogToFile=false.
if (builder.Configuration.GetValue("Agent:LogToFile", true))
{
    var stateDirectory = builder.Configuration["Agent:StateDirectory"];
    var logDirectory = Path.Combine(string.IsNullOrWhiteSpace(stateDirectory) ? AgentStateStore.DefaultDirectory() : stateDirectory, "logs");
    builder.Logging.AddProvider(new FileLoggerProvider(logDirectory));
}

// Läuft als Windows-Dienst bzw. systemd-Dienst; beim Start aus der Konsole sind beide Aufrufe wirkungslos.
builder.Services.AddWindowsService(o => o.ServiceName = "OpenDeviceManagerAgent");
builder.Services.AddSystemd();

builder.Services.AddOptions<AgentOptions>()
    .Bind(builder.Configuration.GetSection(AgentOptions.SectionName))
    .Validate(o => o.Validate() is null, "Invalid agent configuration (ServerUrl).")
    .ValidateOnStart();

builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
    var certificates = sp.GetRequiredService<ClientCertificateStore>();
    var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };

    // Client-Zertifikat (mTLS) wird erst beim Verbindungsaufbau gewählt, damit ein später ausgestelltes oder erneuertes
    // Zertifikat ohne Neustart gilt. Ohne Zertifikat verbindet sich der Agent wie bisher nur mit seinem Secret.
    handler.SslOptions.LocalCertificateSelectionCallback = (_, _, _, _, _) => certificates.Current!;
    return new HttpClient(handler)
    {
        BaseAddress = new Uri(options.ServerUrl.TrimEnd('/') + "/"),
        Timeout = TimeSpan.FromSeconds(30)
    };
});

builder.Services.AddSingleton<ServerClient>();
builder.Services.AddSingleton<Odm.Agent.Update.AgentUpdater>();
builder.Services.AddSingleton<AgentStateStore>();
builder.Services.AddSingleton<ClientCertificateStore>();
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
