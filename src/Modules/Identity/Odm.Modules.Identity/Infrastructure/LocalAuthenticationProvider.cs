using Microsoft.EntityFrameworkCore;
using Odm.Modules.Identity.Application;
using Odm.Modules.Identity.Domain;

namespace Odm.Modules.Identity.Infrastructure;

/// <summary>Authentisierung gegen die lokale Datenbank.</summary>
public sealed class LocalAuthenticationProvider(IdentityDbContext db, IPasswordHasher hasher) : IAuthenticationProvider
{
    // Gegen Timing-Angriffe zur Benutzer-Enumeration: auch bei unbekanntem Benutzer wird gehasht.
    private static string? _dummyHash;

    public string Name => "local";

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
