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
using Odm.Modules.Identity.Infrastructure.Ldap;
using Odm.Modules.Identity.Infrastructure.Oidc;
using Odm.SharedKernel;
using Odm.SharedKernel.Contracts;
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
            .Validate(o => o.Secret.Length >= 32, "Jwt:Secret must be at least 32 characters long.")
            .ValidateOnStart();

        services.Configure<BootstrapAdminOptions>(configuration.GetSection("Identity:BootstrapAdmin"));

        services.AddDbContext<IdentityDbContext>((sp, o) =>
            o.UseOdmDatabase(sp.GetRequiredService<IOptions<DatabaseOptions>>().Value, IdentityDbContext.Schema));

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<IAuthenticationProvider, LocalAuthenticationProvider>();
        services.AddScoped<AuthService>();

        // Optional: sign-in against a directory. Local accounts take precedence (see IAuthenticationProvider.OwnsUserAsync).
        services.AddOptions<LdapOptions>()
            .Bind(configuration.GetSection(LdapOptions.SectionName))
            .Validate(o => !o.Enabled || o.Problem() is null,
                "Identity:Ldap is incomplete (Host, BaseDn, UserFilter with placeholder, BindPassword, at least one group).")
            .ValidateOnStart();

        if (configuration.GetValue<bool>($"{LdapOptions.SectionName}:Enabled"))
        {
            services.AddSingleton<ILdapDirectory, SystemLdapDirectory>();
            services.AddScoped<IAuthenticationProvider, LdapAuthenticationProvider>();
        }

        // Optional: sign-in through OpenID Connect (Entra ID, authentik, Keycloak ...). Nothing happens without a provider.
        services.AddOptions<OidcOptions>()
            .Bind(configuration.GetSection(OidcOptions.SectionName))
            .Validate(o => o.Problem() is null,
                "Identity:Oidc is incomplete (PublicBaseUrl, per provider Id, Authority, ClientId and at least one role value).")
            .ValidateOnStart();
        services.AddHttpClient(OidcService.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15));
        services.AddSingleton<OidcStateProtector>();
        services.AddSingleton<OidcService>();

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

        group.MapPost("/login", async (LoginRequest request, AuthService auth, HttpContext http, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrEmpty(request.Password))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["credentials"] = ["User name and password are required."]
                    });
                }

                var response = await auth.LoginAsync(request.UserName, request.Password, ct);
                await AuditSignInAsync(http, "auth.login", request.UserName, response, null, ct);
                return response is null ? Results.Unauthorized() : Results.Ok(response);
            })
            .AllowAnonymous()
            .RequireRateLimiting("login");

        MapOidc(group);
        Endpoints.UserEndpoints.Map(endpoints);

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

    /// <summary>Records a sign-in attempt (the Audit module is optional). The password is never part of the entry.</summary>
    private static Task AuditSignInAsync(
        HttpContext http, string action, string? userName, LoginResponse? response, string? error, CancellationToken ct)
    {
        var audit = http.RequestServices.GetService<IAuditLog>();
        if (audit is null)
        {
            return Task.CompletedTask;
        }

        var name = (userName ?? "").Trim();
        return audit.RecordAsync(new AuditEvent(
            action,
            response is null ? "failed" : "success",
            Actor: name.Length > 0 ? name : "unknown",
            Role: response?.User.Role,
            Details: response is null ? error : null), ct);
    }

    private const string OidcCookie = "odm_oidc";
    private const string OidcCookiePath = "/api/auth/oidc";

    private static void MapOidc(RouteGroupBuilder group)
    {
        group.MapGet("/oidc/providers", (OidcService oidc) =>
                Results.Ok(oidc.Providers.Select(p => new OidcProviderDto(p.Id, p.DisplayName))))
            .AllowAnonymous();

        // Start: the browser is sent to the provider. The sign-in state lives in a short-lived, signed cookie.
        group.MapGet("/oidc/{provider:regex(^[a-z0-9-]{{1,32}}$)}/login", async (
                string provider, OidcService oidc, IOptions<OidcOptions> options, HttpContext http, CancellationToken ct) =>
            {
                var start = await oidc.StartAsync(provider, ct);
                if (start is null)
                {
                    return Results.NotFound();
                }

                http.Response.Headers.CacheControl = "no-store";
                http.Response.Cookies.Append(OidcCookie, start.StateCookie, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = options.Value.SecureCookies,
                    SameSite = SameSiteMode.Lax, // returning from the provider is a navigation from outside
                    Path = OidcCookiePath,
                    MaxAge = TimeSpan.FromMinutes(10),
                    IsEssential = true
                });
                return Results.Redirect(start.AuthorizationUrl);
            })
            .AllowAnonymous()
            .RequireRateLimiting("login");

        // Return: exchange the code for a token, validate, issue an ODM token. The token travels in the fragment (#) of the
        // address back to the UI; a fragment is never sent to a server. The UI removes it immediately.
        group.MapGet("/oidc/callback", async (
                string? code, string? state, string? error, OidcService oidc, IOptions<OidcOptions> options,
                HttpContext http, CancellationToken ct) =>
            {
                var cookie = http.Request.Cookies[OidcCookie];
                http.Response.Cookies.Delete(OidcCookie, new CookieOptions
                {
                    Path = OidcCookiePath,
                    Secure = options.Value.SecureCookies,
                    SameSite = SameSiteMode.Lax
                });
                http.Response.Headers.CacheControl = "no-store";

                if (!string.IsNullOrEmpty(error))
                {
                    return Results.Redirect("/#/login?oidc=failed");
                }

                var result = await oidc.CompleteAsync(code, state, cookie, ct);
                await AuditSignInAsync(http, "auth.login.oidc", result.Response?.User.UserName, result.Response, result.Error, ct);
                if (result.Response is null)
                {
                    return Results.Redirect($"/#/login?oidc={result.Error ?? "failed"}");
                }

                return Results.Redirect(
                    $"/#/oidc?token={Uri.EscapeDataString(result.Response.Token)}" +
                    $"&expires={Uri.EscapeDataString(result.Response.ExpiresAt.ToString("O"))}");
            })
            .AllowAnonymous()
            .RequireRateLimiting("login");
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<IdentityDbContext>();
        var logger = sp.GetRequiredService<ILogger<IdentityModule>>();

        await db.MigrateOdmAsync(ct);

        if (await db.Users.AnyAsync(ct))
        {
            return;
        }

        var bootstrap = sp.GetRequiredService<IOptions<BootstrapAdminOptions>>().Value;
        if (string.IsNullOrWhiteSpace(bootstrap.UserName) || string.IsNullOrEmpty(bootstrap.Password))
        {
            logger.LogWarning(
                "No users exist and Identity:BootstrapAdmin is not configured - sign-in is not possible.");
            return;
        }

        var hasher = sp.GetRequiredService<IPasswordHasher>();
        db.Users.Add(User.Create(bootstrap.UserName, "Administrator", hasher.Hash(bootstrap.Password), UserRole.Admin));
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Initial administrator '{UserName}' was created.", bootstrap.UserName);
    }
}
