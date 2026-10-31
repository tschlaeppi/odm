namespace Odm.Agent.Os;

/// <summary>
/// Runs a package manager in the context of the signed-in user (scope "user"). Windows: process in the user's console session.
/// Linux: <c>runuser</c> with the user's home and runtime directory, so that per-user installations (e.g. Flatpak --user) work.
/// </summary>
public static class UserProcessRunner
{
    public static async Task<ProcessRunner.Result> RunAsync(
        string tool, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            return await WindowsUserProcess.RunAsync(tool, arguments, timeout, ct);
        }

        var user = await FindLinuxUserAsync(ct)
            ?? throw new UserContextException("No user is signed in at a graphical or console session of this device.");
        var passwd = await ProcessRunner.RunAsync("/usr/bin/getent", ["passwd", user.UserName], TimeSpan.FromSeconds(10), ct);
        var home = UserSessions.ParseHome(passwd.Output)
            ?? throw new UserContextException($"Home directory of user {user.UserName} not found.");
        var runuser = Repo.ToolLocator.Linux("runuser")
            ?? throw new UserContextException("runuser is not available on this device.");

        var runtime = $"/run/user/{user.Uid}";
        List<string> command =
        [
            "-u", user.UserName, "--", "/usr/bin/env",
            $"HOME={home}", $"XDG_RUNTIME_DIR={runtime}", $"DBUS_SESSION_BUS_ADDRESS=unix:path={runtime}/bus", tool,
        ];
        command.AddRange(arguments);
        return await ProcessRunner.RunAsync(runuser, command, timeout, ct);
    }

    private static async Task<LinuxUser?> FindLinuxUserAsync(CancellationToken ct)
    {
        var loginctl = Repo.ToolLocator.Linux("loginctl");
        if (loginctl is null)
        {
            return null;
        }

        var result = await ProcessRunner.RunAsync(loginctl, ["list-sessions", "--no-legend"], TimeSpan.FromSeconds(10), ct);
        return result.ExitCode == 0 ? UserSessions.ParseLoginctl(result.Output) : null;
    }
}
