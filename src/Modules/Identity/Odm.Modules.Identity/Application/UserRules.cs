using System.Text.RegularExpressions;
using Odm.Modules.Identity.Domain;

namespace Odm.Modules.Identity.Application;

/// <summary>Validation and protection rules for local user management (pure, testable).</summary>
public static partial class UserRules
{
    public const int MinPasswordLength = 10;
    public const int MaxPasswordLength = 256;
    public const int MaxUserNameLength = 128;
    public const int MaxDisplayNameLength = 256;

    public static bool IsValidUserName(string? userName) =>
        !string.IsNullOrWhiteSpace(userName) && userName.Trim().Length <= MaxUserNameLength && UserNamePattern().IsMatch(userName.Trim());

    public static bool IsValidPassword(string? password) =>
        password is { Length: >= MinPasswordLength and <= MaxPasswordLength };

    /// <summary>
    /// Would the change take the target out of the group of active administrators (demotion, deactivation or removal)?
    /// Together with "no other active administrator exists" this blocks the change, so the system cannot lock itself out.
    /// </summary>
    public static bool RemovesActiveAdmin(User target, UserRole newRole, bool newActive, bool removed) =>
        target.Role == UserRole.Admin && target.IsActive && (removed || newRole != UserRole.Admin || !newActive);

    [GeneratedRegex(@"^[A-Za-z0-9._@\-]+$")]
    private static partial Regex UserNamePattern();
}
