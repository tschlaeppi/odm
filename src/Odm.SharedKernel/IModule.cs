using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Odm.SharedKernel;

/// <summary>
/// Entry point of a module in the modular monolith.
/// Modules do not know each other; the host application wires them together.
/// </summary>
public interface IModule
{
    string Name { get; }

    void ConfigureServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>Registers the endpoints relative to the given group (e.g. "/api").</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints);

    /// <summary>Runs once at startup (migrations, initial data).</summary>
    Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken);
}
