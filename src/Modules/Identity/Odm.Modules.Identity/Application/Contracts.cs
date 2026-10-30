using Odm.Modules.Identity.Domain;

namespace Odm.Modules.Identity.Application;

public sealed record LoginRequest(string UserName, string Password);

public sealed record UserDto(Guid Id, string UserName, string DisplayName, string Role);

public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt, UserDto User);

/// <summary>Ergebnis einer erfolgreichen Authentisierung, unabhängig vom Provider.</summary>
public sealed record AuthenticatedUser(Guid Id, string UserName, string DisplayName, UserRole Role)
{
    public UserDto ToDto() => new(Id, UserName, DisplayName, Role.ToString());
}

/// <summary>
/// Erweiterungspunkt: lokale DB heute, LDAP und OData-Endpoint in einer späteren Etappe.
/// </summary>
public interface IAuthenticationProvider
{
    string Name { get; }

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
