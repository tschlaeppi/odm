using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Odm.Modules.Identity.Application;
using Odm.Modules.Identity.Domain;
using Odm.Modules.Identity.Infrastructure;
using Odm.SharedKernel;
using Odm.SharedKernel.Persistence;

namespace Odm.Modules.Identity;

public sealed class BootstrapAdminOptions
{
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class IdentityModule : IModule
{
    public string Name => "Identity";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => o.Secret.Length >= 32, "Jwt:Secret muss mindestens 32 Zeichen lang sein.")
            .ValidateOnStart();

        services.Configure<BootstrapAdminOptions>(configuration.GetSection("Identity:BootstrapAdmin"));

        services.AddDbContext<IdentityDbContext>((sp, o) =>
            o.UseOdmDatabase(sp.GetRequiredService<IOptions<DatabaseOptions>>().Value, IdentityDbContext.Schema));

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<IAuthenticationProvider, LocalAuthenticationProvider>();
        services.AddScoped<AuthService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                var o = jwt.Value;
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = o.Issuer,
                    ValidAudience = o.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Secret)),
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = "name",
                    RoleClaimType = "role"
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Admin, p => p.RequireRole(nameof(UserRole.Admin)))
            .AddPolicy(Policies.Operator, p => p.RequireRole(nameof(UserRole.Operator), nameof(UserRole.Admin)));

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/login", async (LoginRequest request, AuthService auth, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrEmpty(request.Password))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["credentials"] = ["Benutzername und Passwort sind erforderlich."]
                    });
                }

                var response = await auth.LoginAsync(request.UserName, request.Password, ct);
                return response is null ? Results.Unauthorized() : Results.Ok(response);
            })
            .AllowAnonymous()
            .RequireRateLimiting("login");

        group.MapGet("/me", (ClaimsPrincipal principal) =>
            {
                var id = Guid.TryParse(principal.FindFirstValue("sub"), out var guid) ? guid : Guid.Empty;
                return Results.Ok(new UserDto(
                    id,
                    principal.FindFirstValue("name") ?? "",
                    principal.FindFirstValue("display_name") ?? "",
                    principal.FindFirstValue("role") ?? ""));
            })
            .RequireAuthorization();
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<IdentityDbContext>();
        var logger = sp.GetRequiredService<ILogger<IdentityModule>>();

        await db.Database.MigrateAsync(ct);

        if (await db.Users.AnyAsync(ct))
        {
            return;
        }

        var bootstrap = sp.GetRequiredService<IOptions<BootstrapAdminOptions>>().Value;
        if (string.IsNullOrWhiteSpace(bootstrap.UserName) || string.IsNullOrEmpty(bootstrap.Password))
        {
            logger.LogWarning(
                "Keine Benutzer vorhanden und kein Identity:BootstrapAdmin konfiguriert - Anmeldung nicht möglich.");
            return;
        }

        var hasher = sp.GetRequiredService<IPasswordHasher>();
        db.Users.Add(User.Create(bootstrap.UserName, "Administrator", hasher.Hash(bootstrap.Password), UserRole.Admin));
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Initialer Administrator '{UserName}' wurde angelegt.", bootstrap.UserName);
    }
}
