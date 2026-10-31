using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Odm.Modules.Identity.Application;
using Odm.Modules.Identity.Domain;
using Odm.Modules.Identity.Infrastructure;
using Odm.SharedKernel;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.Identity.Endpoints;

/// <summary>Management of local users (administrators) and the own password (every local user).</summary>
internal static class UserEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var users = endpoints.MapGroup("/users").WithTags("Users").RequireAuthorization(Policies.Admin);

        users.MapGet("/", async (IdentityDbContext db, CancellationToken ct) =>
        {
            var rows = await db.Users.AsNoTracking().OrderBy(u => u.UserName).ToListAsync(ct);
            return Results.Ok(rows.Select(ToDto));
        });

        users.MapPost("/", async (CreateUserRequest request, IdentityDbContext db, IPasswordHasher hasher, CancellationToken ct) =>
        {
            if (!UserRules.IsValidUserName(request.UserName))
            {
                return Invalid("userName", "User name is required (letters, digits and . _ @ -, at most 128 characters).");
            }

            if (!UserRules.IsValidPassword(request.Password))
            {
                return Invalid("password", "Password must be 10 to 256 characters long.");
            }

            if (!TryRole(request.Role, out var role))
            {
                return Invalid("role", "Invalid role.");
            }

            var displayName = request.DisplayName?.Trim() ?? "";
            if (displayName.Length > UserRules.MaxDisplayNameLength)
            {
                return Invalid("displayName", "Display name must be at most 256 characters.");
            }

            var name = User.Normalize(request.UserName!);
            if (await db.Users.AnyAsync(u => u.UserName == name, ct))
            {
                return Invalid("userName", "A user with this name already exists.", StatusCodes.Status409Conflict);
            }

            var user = User.Create(name, displayName, hasher.Hash(request.Password!), role);
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/users/{user.Id}", ToDto(user));
        });

        users.MapPut("/{id:guid}", async (
            Guid id, UpdateUserRequest request, ClaimsPrincipal principal, IdentityDbContext db, CancellationToken ct) =>
        {
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct);
            if (user is null)
            {
                return Results.NotFound();
            }

            var role = user.Role;
            if (request.Role is not null && !TryRole(request.Role, out role))
            {
                return Invalid("role", "Invalid role.");
            }

            var active = request.IsActive ?? user.IsActive;
            var displayName = request.DisplayName ?? user.DisplayName;
            if (displayName.Trim().Length > UserRules.MaxDisplayNameLength)
            {
                return Invalid("displayName", "Display name must be at most 256 characters.");
            }

            if (IsSelf(principal, id) && (role != user.Role || !active))
            {
                return Invalid("user", "You cannot change your own role or deactivate yourself.", StatusCodes.Status409Conflict);
            }

            if (UserRules.RemovesActiveAdmin(user, role, active, removed: false) && !await HasOtherActiveAdminAsync(db, id, ct))
            {
                return Invalid("user", "The last active administrator cannot be removed, demoted or deactivated.", StatusCodes.Status409Conflict);
            }

            user.Update(displayName, role, active);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(user));
        });

        // Administrator sets a new password (forgotten password).
        users.MapPost("/{id:guid}/password", async (
            Guid id, SetPasswordRequest request, IdentityDbContext db, IPasswordHasher hasher, CancellationToken ct) =>
        {
            if (!UserRules.IsValidPassword(request.Password))
            {
                return Invalid("password", "Password must be 10 to 256 characters long.");
            }

            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct);
            if (user is null)
            {
                return Results.NotFound();
            }

            user.SetPasswordHash(hasher.Hash(request.Password!));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        users.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal principal, IdentityDbContext db, CancellationToken ct) =>
        {
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct);
            if (user is null)
            {
                return Results.NotFound();
            }

            if (IsSelf(principal, id))
            {
                return Invalid("user", "You cannot delete yourself.", StatusCodes.Status409Conflict);
            }

            if (UserRules.RemovesActiveAdmin(user, user.Role, user.IsActive, removed: true) && !await HasOtherActiveAdminAsync(db, id, ct))
            {
                return Invalid("user", "The last active administrator cannot be removed, demoted or deactivated.", StatusCodes.Status409Conflict);
            }

            db.Users.Remove(user);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Own password (local users only). Rate limited like the sign-in; recorded in the audit log explicitly
        // because the audit middleware skips /api/auth.
        endpoints.MapPost("/auth/password", async (
                ChangePasswordRequest request, ClaimsPrincipal principal, HttpContext http,
                IdentityDbContext db, IPasswordHasher hasher, CancellationToken ct) =>
            {
                var user = Guid.TryParse(principal.FindFirstValue("sub"), out var id)
                    ? await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct)
                    : null;
                if (user is null)
                {
                    return Invalid("password", "Password change is only available for local users.");
                }

                if (string.IsNullOrEmpty(request.CurrentPassword) || !hasher.Verify(request.CurrentPassword, user.PasswordHash))
                {
                    await AuditAsync(http, user, "failed", ct);
                    return Invalid("currentPassword", "The current password is wrong.");
                }

                if (!UserRules.IsValidPassword(request.NewPassword))
                {
                    return Invalid("newPassword", "Password must be 10 to 256 characters long.");
                }

                user.SetPasswordHash(hasher.Hash(request.NewPassword!));
                await db.SaveChangesAsync(ct);
                await AuditAsync(http, user, "success", ct);
                return Results.NoContent();
            })
            .RequireAuthorization()
            .RequireRateLimiting("login");
    }

    private static Task AuditAsync(HttpContext http, User user, string outcome, CancellationToken ct) =>
        http.RequestServices.GetService<IAuditLog>() is { } audit
            ? audit.RecordAsync(new AuditEvent("auth.password.change", outcome, Actor: user.UserName, Role: user.Role.ToString()), ct)
            : Task.CompletedTask;

    private static bool IsSelf(ClaimsPrincipal principal, Guid id) =>
        Guid.TryParse(principal.FindFirstValue("sub"), out var self) && self == id;

    private static Task<bool> HasOtherActiveAdminAsync(IdentityDbContext db, Guid excludeId, CancellationToken ct) =>
        db.Users.AnyAsync(u => u.Id != excludeId && u.Role == UserRole.Admin && u.IsActive, ct);

    private static bool TryRole(string? text, out UserRole role) =>
        Enum.TryParse(text, ignoreCase: true, out role) && Enum.IsDefined(role);

    private static ManagedUserDto ToDto(User u) => new(u.Id, u.UserName, u.DisplayName, u.Role.ToString(), u.IsActive, u.CreatedAt);

    private static IResult Invalid(string field, string message, int status = StatusCodes.Status400BadRequest) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] }, statusCode: status);
}
