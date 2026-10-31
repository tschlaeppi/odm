using Odm.SharedKernel;

namespace Odm.Modules.Identity.Domain;

public enum UserRole
{
    ReadOnly = 0,
    Operator = 1,
    Admin = 2
}

/// <summary>Local ODM user (sign-in to the management UI).</summary>
public sealed class User : Entity
{
    public string UserName { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public string PasswordHash { get; private set; } = "";
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }

    private User() { }

    public static string Normalize(string userName) => userName.Trim().ToLowerInvariant();

    public static User Create(string userName, string displayName, string passwordHash, UserRole role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        return new User
        {
            UserName = Normalize(userName),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? userName.Trim() : displayName.Trim(),
            PasswordHash = passwordHash,
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
