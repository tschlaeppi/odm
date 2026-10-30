using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Odm.SharedKernel;

/// <summary>
/// Einstiegspunkt eines Moduls im modularen Monolithen.
/// Module kennen sich nicht gegenseitig; die Host-Anwendung verdrahtet sie.
/// </summary>
public interface IModule
{
    string Name { get; }

    void ConfigureServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>Registriert die Endpunkte relativ zur übergebenen Gruppe (z. B. "/api").</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints);

    /// <summary>Wird einmal beim Start ausgeführt (Migrationen, Initialdaten).</summary>
    Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken);
}
