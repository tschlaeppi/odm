using System.Text.RegularExpressions;

namespace Odm.Tests;

/// <summary>
/// Guards that a change cannot quietly add anonymous endpoints or new code paths that start programs or open network
/// connections from the server modules. A legitimate new entry must be added here deliberately, which shows up in the review.
/// </summary>
public class SecurityGuardTests
{
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenDeviceManager.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static IEnumerable<string> SourceFiles(string relative) =>
        Directory.EnumerateFiles(Path.Combine(Root(), relative), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"));

    private static Dictionary<string, int> Count(string relative, string pattern) =>
        SourceFiles(relative)
            .Select(f => (File: Path.GetRelativePath(Root(), f).Replace('\\', '/'), Count: Regex.Matches(File.ReadAllText(f), pattern).Count))
            .Where(x => x.Count > 0)
            .ToDictionary(x => x.File, x => x.Count);

    [Fact]
    public void Anonymous_endpoints_are_the_known_ones()
    {
        var actual = Count("src", @"\.AllowAnonymous\(\)");

        var expected = new Dictionary<string, int>
        {
            ["src/Modules/Identity/Odm.Modules.Identity/IdentityModule.cs"] = 4, // sign-in, provider list, OIDC start and callback
            ["src/Modules/Agents/Odm.Modules.Agents/Endpoints/InstallEndpoints.cs"] = 1, // install scripts and agent downloads
            ["src/Modules/Agents/Odm.Modules.Agents/Endpoints/AgentEndpoints.cs"] = 1 // registration with a one-time token
        };

        Assert.Equal(expected.OrderBy(x => x.Key), actual.OrderBy(x => x.Key));
    }

    [Fact]
    public void Server_modules_do_not_start_processes()
    {
        Assert.Empty(Count("src/Modules", @"Process\.Start|new\s+ProcessStartInfo|ProcessRunner"));
        Assert.Empty(Count("src/Odm.Host", @"Process\.Start|new\s+ProcessStartInfo|ProcessRunner"));
    }
}
