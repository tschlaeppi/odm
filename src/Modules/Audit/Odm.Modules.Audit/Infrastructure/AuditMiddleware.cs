using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.Audit.Infrastructure;

/// <summary>Puts the audit middleware at the very start of the pipeline so that it sees the final status code.</summary>
internal sealed class AuditStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseMiddleware<AuditMiddleware>();
        next(app);
    };
}

/// <summary>
/// Records every state-changing API request (POST, PUT, PATCH, DELETE) with user, route, outcome and client address.
/// Request bodies are never stored, so no secret values end up in the log. Sign-in is recorded explicitly by the
/// Identity module (it knows the user name); agent traffic is not recorded except registration and certificates.
/// </summary>
internal sealed partial class AuditMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsCandidate(context.Request))
        {
            await next(context);
            return;
        }

        try
        {
            await next(context);
        }
        finally
        {
            await RecordAsync(context);
        }
    }

    private static bool IsCandidate(HttpRequest request) =>
        request.Path.StartsWithSegments("/api") &&
        (HttpMethods.IsPost(request.Method) || HttpMethods.IsPut(request.Method) ||
         HttpMethods.IsPatch(request.Method) || HttpMethods.IsDelete(request.Method));

    private static async Task RecordAsync(HttpContext context)
    {
        var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? context.Request.Path.Value ?? "";
        route = Constraint().Replace(route, "{$1}");

        if (!AuditRules.ShouldRecord(route))
        {
            return;
        }

        var audit = context.RequestServices.GetService<IAuditLog>();
        if (audit is null)
        {
            return;
        }

        var status = context.Response.StatusCode;
        var outcome = status switch
        {
            < 400 => "success",
            401 or 403 => "denied",
            < 500 => "failed",
            _ => "error"
        };

        var action = $"{context.Request.Method.ToUpperInvariant()} {route.TrimStart('/').RemovePrefix("api/")}";
        await audit.RecordAsync(new AuditEvent(action, outcome, Target: context.Request.Path.Value));
    }

    /// <summary><c>{id:guid}</c> becomes <c>{id}</c>.</summary>
    [GeneratedRegex(@"\{([^}:?*]+)[^}]*\}")]
    private static partial Regex Constraint();
}

/// <summary>Which API routes end up in the audit log.</summary>
public static class AuditRules
{
    /// <summary>Sign-in is recorded by Identity; agent traffic only for registration and certificates; previews change nothing.</summary>
    public static bool ShouldRecord(string route)
    {
        if (route.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase) ||
            route.EndsWith("/preview", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (route.StartsWith("/api/agent/", StringComparison.OrdinalIgnoreCase))
        {
            return route.EndsWith("/register", StringComparison.OrdinalIgnoreCase) ||
                   route.EndsWith("/certificate", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }
}

internal static class StringExtensions
{
    public static string RemovePrefix(this string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? value[prefix.Length..] : value;
}
