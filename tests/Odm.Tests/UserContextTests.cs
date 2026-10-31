using Odm.Agent.Os;
using Odm.Agent.Repo;
using Xunit;

namespace Odm.Tests;

public class UserContextTests
{
    [Fact]
    public void Loginctl_picks_the_user_on_a_seat_and_ignores_ssh_and_system_users()
    {
        const string output =
            "  c1   0    root   -     pts/0\n" +
            "  c2   1001 remote -     pts/1\n" +
            "  2    1000 anna   seat0 tty2\n";

        var user = UserSessions.ParseLoginctl(output);

        Assert.Equal(new LinuxUser("anna", 1000), user);
    }

    [Fact]
    public void Loginctl_without_seat_session_yields_nobody() =>
        Assert.Null(UserSessions.ParseLoginctl("  c1 1001 remote - pts/1\n"));

    [Fact]
    public void Home_is_read_from_passwd_line()
    {
        Assert.Equal("/home/anna", UserSessions.ParseHome("anna:x:1000:1000:Anna:/home/anna:/bin/bash\n"));
        Assert.Null(UserSessions.ParseHome("garbage"));
    }

    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("with space", "\"with space\"")]
    [InlineData("", "\"\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("C:\\dir with space\\", "\"C:\\dir with space\\\\\"")]
    public void Windows_arguments_are_quoted(string argument, string expected) =>
        Assert.Equal(expected, UserSessions.QuoteWindowsArgument(argument));

    [Theory]
    [InlineData("Winget", "user", true)]
    [InlineData("Flatpak", "user", true)]
    [InlineData("Winget", "machine", false)]
    [InlineData("Flatpak", "system", false)]
    [InlineData("Chocolatey", "user", false)]
    public void Only_winget_and_flatpak_user_scope_run_as_user(string manager, string scope, bool expected)
    {
        var payload = new InstallRepoPayload(manager, "id", null, scope, null, null, null, false, null, 10);
        Assert.Equal(expected, RepoInstaller.RunsAsUser(payload));
    }
}
