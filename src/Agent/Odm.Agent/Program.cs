using Microsoft.Extensions.Options;
using Odm.Agent;
using Odm.Agent.Jobs;
using Odm.Agent.Os;

var builder = Host.CreateApplicationBuilder(args);

// Configuration written by the install script (ServerUrl, one-time token) in the protected state directory.
// It takes precedence over appsettings.json and environment variables; if it is missing, the usual configuration applies.
// If it is not readable for the current user (agent started by hand without administrator rights), it is skipped.
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

// Log file in the state directory (Windows: C:\ProgramData\OpenDeviceManager\logs, Linux: /var/lib/odm-agent/logs),
// in addition to console/journald or the Event Log. Can be disabled with Agent:LogToFile=false.
if (builder.Configuration.GetValue("Agent:LogToFile", true))
{
    var stateDirectory = builder.Configuration["Agent:StateDirectory"];
    var logDirectory = Path.Combine(string.IsNullOrWhiteSpace(stateDirectory) ? AgentStateStore.DefaultDirectory() : stateDirectory, "logs");
    builder.Logging.AddProvider(new FileLoggerProvider(logDirectory));
}

// Runs as Windows service or systemd service; when started from a console both calls have no effect.
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

    // The client certificate (mTLS) is only chosen when the connection is established, so that a certificate issued or renewed
    // later applies without a restart. Without a certificate the agent connects with its secret only, as before.
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
builder.Services.AddSingleton<Odm.Agent.Inventory.InventoryCollector>();
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
