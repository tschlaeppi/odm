using Microsoft.EntityFrameworkCore;
using Odm.Modules.Identity.Application;
using Odm.Modules.Identity.Domain;

namespace Odm.Modules.Identity.Infrastructure;

/// <summary>Authentication against the local database.</summary>
public sealed class LocalAuthenticationProvider(IdentityDbContext db, IPasswordHasher hasher) : IAuthenticationProvider
{
    // Against timing attacks for user enumeration: hash even for an unknown user.
    private static string? _dummyHash;

    public string Name => "local";

    public Task<bool> OwnsUserAsync(string userName, CancellationToken ct)
    {
        var normalized = User.Normalize(userName);
        return db.Users.AsNoTracking().AnyAsync(u => u.UserName == normalized, ct);
    }

    public async Task<AuthenticatedUser?> AuthenticateAsync(string userName, string password, CancellationToken ct)
    {
        var normalized = User.Normalize(userName);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.UserName == normalized, ct);

        if (user is null)
        {
            _dummyHash ??= hasher.Hash("dummy-password");
            hasher.Verify(password, _dummyHash);
            return null;
        }

        if (!user.IsActive || !hasher.Verify(password, user.PasswordHash))
        {
            return null;
        }

        return new AuthenticatedUser(user.Id, user.UserName, user.DisplayName, user.Role);
    }
}
