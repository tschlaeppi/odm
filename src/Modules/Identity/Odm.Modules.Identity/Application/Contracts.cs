using Odm.Modules.Identity.Domain;

namespace Odm.Modules.Identity.Application;

public sealed record LoginRequest(string UserName, string Password);

public sealed record UserDto(Guid Id, string UserName, string DisplayName, string Role);

public sealed record OidcProviderDto(string Id, string DisplayName);

public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt, UserDto User);

/// <summary>Ergebnis einer erfolgreichen Authentisierung, unabhängig vom Provider.</summary>
public sealed record AuthenticatedUser(Guid Id, string UserName, string DisplayName, UserRole Role)
{
    public UserDto ToDto() => new(Id, UserName, DisplayName, Role.ToString());
}

/// <summary>
/// Erweiterungspunkt für Anmeldeverfahren: lokale Datenbank, LDAP und weitere.
/// </summary>
public interface IAuthenticationProvider
{
    string Name { get; }

    /// <summary>
    /// Ist der Benutzername bei diesem Verfahren vergeben? Dann entscheidet nur dieses Verfahren: ein lokales Konto
    /// mit falschem Passwort darf nicht von einem gleichnamigen Konto im Verzeichnis "gerettet" werden.
    /// </summary>
    Task<bool> OwnsUserAsync(string userName, CancellationToken ct) => Task.FromResult(false);

    Task<AuthenticatedUser?> AuthenticateAsync(string userName, string password, CancellationToken ct);
}

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string hash);
}

public interface ITokenService
{
    (string Token, DateTimeOffset ExpiresAt) Create(AuthenticatedUser user);
}
