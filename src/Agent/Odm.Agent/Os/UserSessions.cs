namespace Odm.Agent.Os;

/// <summary>A signed-in user on a Linux seat (graphical or console session).</summary>
public sealed record LinuxUser(string UserName, int Uid);

/// <summary>Parsing helpers for finding the signed-in user; kept pure so they can be tested.</summary>
public static class UserSessions
{
    /// <summary>
    /// Picks the user of the first session on a seat from <c>loginctl list-sessions --no-legend</c>
    /// (columns SESSION UID USER SEAT [TTY]); remote sessions (SSH) have no seat and are ignored.
    /// </summary>
    public static LinuxUser? ParseLoginctl(string output)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var columns = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length >= 4 && int.TryParse(columns[1], out var uid) && uid >= 1000 && columns[3].StartsWith("seat", StringComparison.Ordinal))
            {
                return new LinuxUser(columns[2], uid);
            }
        }

        return null;
    }

    /// <summary>Home directory from a <c>getent passwd</c> line (name:x:uid:gid:gecos:home:shell).</summary>
    public static string? ParseHome(string passwdLine)
    {
        var fields = passwdLine.Trim().Split(':');
        return fields.Length >= 6 && fields[5].StartsWith('/') ? fields[5] : null;
    }

    /// <summary>Quotes arguments for a Windows command line (rules of CommandLineToArgvW).</summary>
    public static string QuoteWindowsArguments(IEnumerable<string> arguments) =>
        string.Join(' ', arguments.Select(QuoteWindowsArgument));

    public static string QuoteWindowsArgument(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            return argument;
        }

        var result = new System.Text.StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                result.Append('\\', backslashes * 2 + 1).Append('"');
            }
            else
            {
                result.Append('\\', backslashes).Append(c);
            }

            backslashes = 0;
        }

        result.Append('\\', backslashes * 2).Append('"');
        return result.ToString();
    }
}
