using Odm.Modules.Identity.Domain;

namespace Odm.Modules.Identity.Application;

public sealed record LoginRequest(string UserName, string Password);

public sealed record UserDto(Guid Id, string UserName, string DisplayName, string Role);

public sealed record ManagedUserDto(
    Guid Id, string UserName, string DisplayName, string Role, bool IsActive, DateTimeOffset CreatedAt);

public sealed record CreateUserRequest(string? UserName, string? DisplayName, string? Password, string? Role);

public sealed record UpdateUserRequest(string? DisplayName, string? Role, bool? IsActive);

public sealed record SetPasswordRequest(string? Password);

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public sealed record OidcProviderDto(string Id, string DisplayName);

public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt, UserDto User);

/// <summary>Result of a successful authentication, independent of the provider.</summary>
public sealed record AuthenticatedUser(Guid Id, string UserName, string DisplayName, UserRole Role)
{
    public UserDto ToDto() => new(Id, UserName, DisplayName, Role.ToString());
}

/// <summary>
/// Extension point for sign-in methods: local database, LDAP and more.
/// </summary>
public interface IAuthenticationProvider
{
    string Name { get; }

    /// <summary>
    /// Is the user name taken by this method? Then only this method decides: a local account
    /// with a wrong password must not be "rescued" by an account of the same name in the directory.
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
