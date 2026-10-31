using Odm.Modules.Identity.Application;
using Odm.Modules.Identity.Domain;

namespace Odm.Tests;

public class UserManagementTests
{
    [Theory]
    [InlineData("anna", true)]
    [InlineData("a.meier@example.org", true)]
    [InlineData("t_schlaeppi-1", true)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("a b", false)]
    [InlineData("a/b", false)]
    [InlineData("ä", false)]
    public void User_name_validation(string name, bool valid) => Assert.Equal(valid, UserRules.IsValidUserName(name));

    [Fact]
    public void User_name_longer_than_128_is_invalid() => Assert.False(UserRules.IsValidUserName(new string('a', 129)));

    [Theory]
    [InlineData("123456789", false)]
    [InlineData("1234567890", true)]
    [InlineData(null, false)]
    public void Password_length_is_checked(string? password, bool valid) => Assert.Equal(valid, UserRules.IsValidPassword(password));

    [Fact]
    public void Password_longer_than_256_is_invalid() => Assert.False(UserRules.IsValidPassword(new string('x', 257)));

    [Fact]
    public void Update_changes_fields_and_falls_back_to_user_name_for_empty_display_name()
    {
        var user = User.Create("anna", "Anna", "hash", UserRole.Operator);

        user.Update("  ", UserRole.Admin, false);

        Assert.Equal("anna", user.DisplayName);
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.False(user.IsActive);
    }

    [Fact]
    public void SetPasswordHash_replaces_the_hash_and_rejects_empty()
    {
        var user = User.Create("anna", "Anna", "old", UserRole.Operator);

        user.SetPasswordHash("new");

        Assert.Equal("new", user.PasswordHash);
        Assert.Throws<ArgumentException>(() => user.SetPasswordHash(" "));
    }

    [Theory]
    [InlineData(UserRole.Admin, true, UserRole.Admin, true, false, false)] // unchanged
    [InlineData(UserRole.Admin, true, UserRole.Operator, true, false, true)] // demoted
    [InlineData(UserRole.Admin, true, UserRole.Admin, false, false, true)] // deactivated
    [InlineData(UserRole.Admin, true, UserRole.Admin, true, true, true)] // removed
    [InlineData(UserRole.Operator, true, UserRole.ReadOnly, false, true, false)] // not an admin
    [InlineData(UserRole.Admin, false, UserRole.Operator, true, true, false)] // already inactive
    public void RemovesActiveAdmin_detects_loss_of_an_active_administrator(
        UserRole role, bool active, UserRole newRole, bool newActive, bool removed, bool expected)
    {
        var user = User.Create("u", "U", "hash", role);
        user.Update("U", role, active);

        Assert.Equal(expected, UserRules.RemovesActiveAdmin(user, newRole, newActive, removed));
    }
}
