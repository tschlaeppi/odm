using Odm.Agent.Repo;

namespace Odm.Tests;

public class ArgumentSplitterTests
{
    [Theory]
    [InlineData(null, new string[0])]
    [InlineData("", new string[0])]
    [InlineData("   ", new string[0])]
    [InlineData("--a b  c", new[] { "--a", "b", "c" })]
    [InlineData("--a \"b c\" d", new[] { "--a", "b c", "d" })]
    [InlineData("'x y' z", new[] { "x y", "z" })]
    [InlineData("a\"b c\"d", new[] { "ab cd" })]
    [InlineData("\"\"", new[] { "" })]
    [InlineData("--opt=\"a b\"", new[] { "--opt=a b" })]
    public void Splits_like_a_shell_without_starting_one(string? text, string[] expected) =>
        Assert.Equal(expected, ArgumentSplitter.Split(text));

    [Fact]
    public void Backslashes_in_windows_paths_are_kept()
    {
        Assert.Equal(Arr(@"C:\Program Files\App"), ArgumentSplitter.Split("\"C:\\Program Files\\App\""));
        Assert.Equal(Arr(@"C:\Program", @"Files\App"), ArgumentSplitter.Split(@"C:\Program Files\App"));
    }

    [Fact]
    public void Escaped_quote_inside_double_quotes()
    {
        Assert.Equal(Arr("sag \"hallo\""), ArgumentSplitter.Split("\"sag \\\"hallo\\\"\""));
    }

    [Theory]
    [InlineData("\"offen")]
    [InlineData("'offen")]
    public void Unclosed_quote_is_an_error(string text) =>
        Assert.Throws<FormatException>(() => ArgumentSplitter.Split(text));
}

public class SecretSubstitutionTests
{
    private static readonly Dictionary<string, string> Secrets = new() { ["LIZENZ"] = "abc-123", ["PW"] = "mein pass wort" };

    [Fact]
    public void References_are_replaced_inside_a_token()
    {
        Assert.Equal("--key=abc-123", SecretSubstitution.Apply("--key={{secret:LIZENZ}}", Secrets));
        Assert.Equal("without", SecretSubstitution.Apply("without", Secrets));
    }

    [Fact]
    public void Missing_secret_is_an_error_that_names_the_secret_but_not_a_value()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SecretSubstitution.Apply("{{secret:FEHLT}}", Secrets));

        Assert.Contains("FEHLT", ex.Message);
    }

    [Fact]
    public void A_secret_with_spaces_stays_one_argument()
    {
        // Split first, then substitute
        var tokens = ArgumentSplitter.Split("--password {{secret:PW}}").Select(t => SecretSubstitution.Apply(t, Secrets)).ToList();

        Assert.Equal(Arr("--password", "mein pass wort"), tokens);
    }

    [Fact]
    public void Detects_references()
    {
        Assert.True(SecretSubstitution.HasReferences("x {{secret:A}}"));
        Assert.False(SecretSubstitution.HasReferences("x {secret:A}"));
        Assert.False(SecretSubstitution.HasReferences(null));
    }

    [Fact]
    public void Redact_removes_values_longest_first_and_skips_very_short_ones()
    {
        Assert.Equal("*** ***", SecretSubstitution.Redact("abcdef abc", ["abc", "abcdef"]));
        Assert.Equal("a b ab", SecretSubstitution.Redact("a b ab", ["a", "b"])); // under 3 characters: do not replace
        Assert.Equal("", SecretSubstitution.Redact(null, ["abc"]));
    }
}

public class RepoInstallerArgumentTests
{
    private static InstallRepoPayload Payload(
        string manager, string id, string? version = null, string? scope = null, string? source = null,
        string? remote = null, string? channel = null, bool classic = false) =>
        new(manager, id, version, scope, source, remote, channel, classic, null, 60);

    private static string After(IReadOnlyList<string> args, string option)
    {
        var index = args.ToList().IndexOf(option);
        Assert.True(index >= 0 && index + 1 < args.Count, $"'{option}' is missing or has no value: {string.Join(' ', args)}");
        return args[index + 1];
    }

    [Fact]
    public void Winget_is_unattended_exact_and_machine_scoped_by_default()
    {
        var args = RepoInstaller.WingetArguments(Payload("Winget", "Mozilla.Firefox"), []);

        Assert.Equal("install", args[0]);
        Assert.Equal("Mozilla.Firefox", After(args, "--id"));
        Assert.Contains("--exact", args);
        Assert.Contains("--silent", args);
        Assert.Contains("--disable-interactivity", args);
        Assert.Contains("--accept-package-agreements", args);
        Assert.Contains("--accept-source-agreements", args);
        Assert.Equal("machine", After(args, "--scope"));
        Assert.DoesNotContain("--version", args);
    }

    [Fact]
    public void Winget_with_options_and_extra_arguments_at_the_end()
    {
        var args = RepoInstaller.WingetArguments(
            Payload("Winget", "Vendor.App", version: "1.2", scope: "user", source: "winget"), ["--custom", "/S"]);

        Assert.Equal("user", After(args, "--scope"));
        Assert.Equal("1.2", After(args, "--version"));
        Assert.Equal("winget", After(args, "--source"));
        Assert.Equal(Arr("--custom", "/S"), args.TakeLast(2));
    }

    [Fact]
    public void Chocolatey_upgrade_installs_or_updates_and_allows_downgrade_only_when_pinned()
    {
        var auto = RepoInstaller.ChocolateyArguments(Payload("Chocolatey", "git"), []);
        Assert.Equal(Arr("upgrade", "git"), auto.Take(2));
        Assert.Contains("-y", auto);
        Assert.DoesNotContain("--allow-downgrade", auto);

        var pinned = RepoInstaller.ChocolateyArguments(Payload("Chocolatey", "git", version: "2.40.0"), []);
        Assert.Equal("2.40.0", After(pinned, "--version"));
        Assert.Contains("--allow-downgrade", pinned);
    }

    [Fact]
    public void Apt_target_is_last_and_extra_arguments_come_before_it()
    {
        var auto = RepoInstaller.AptArguments(Payload("Apt", "nginx"), ["--no-install-recommends"]);
        Assert.Equal(Arr("install", "-y", "-q"), auto.Take(3));
        Assert.Equal("nginx", auto[^1]);
        Assert.True(auto.IndexOf("--no-install-recommends") < auto.Count - 1);
        Assert.DoesNotContain("--allow-downgrades", auto);

        var pinned = RepoInstaller.AptArguments(Payload("Apt", "nginx", version: "1.18.0-6"), []);
        Assert.Equal("nginx=1.18.0-6", pinned[^1]);
        Assert.Contains("--allow-downgrades", pinned);
    }

    [Theory]
    [InlineData("dnf", true, null, "upgrade", "git")]
    [InlineData("yum", true, null, "update", "git")]
    [InlineData("dnf", false, null, "install", "git")]
    [InlineData("dnf", true, "2.40", "install", "git-2.40")]
    public void Rpm_picks_install_or_upgrade(string manager, bool installed, string? version, string verb, string target)
    {
        var args = RepoInstaller.RpmArguments(manager, installed, Payload("Rpm", "git", version: version), []);

        Assert.Equal(Arr("-y", verb), args.Take(2));
        Assert.Equal(target, args[^1]);
    }

    [Fact]
    public void Flatpak_install_and_update()
    {
        var install = RepoInstaller.FlatpakInstallArguments(Payload("Flatpak", "org.x.App", remote: "flathub"), []);
        Assert.Equal(Arr("install", "-y", "--noninteractive", "--system", "flathub", "org.x.App"), install);

        var user = RepoInstaller.FlatpakInstallArguments(Payload("Flatpak", "org.x.App", scope: "user"), []);
        Assert.Contains("--user", user);
        Assert.Equal("flathub", user[^2]); // Standard-Remote

        var update = RepoInstaller.FlatpakUpdateArguments(Payload("Flatpak", "org.x.App", version: "abc123"), []);
        Assert.Contains("--commit=abc123", update);
        Assert.Equal("org.x.App", update[^1]);
    }

    [Fact]
    public void Snap_install_refresh_and_classic()
    {
        var install = RepoInstaller.SnapArguments(installed: false, Payload("Snap", "code", channel: "stable", classic: true), []);
        Assert.Equal(Arr("install", "code"), install.Take(2));
        Assert.Contains("--channel=stable", install);
        Assert.Contains("--classic", install);

        var refresh = RepoInstaller.SnapArguments(installed: true, Payload("Snap", "code", version: "5", classic: true), []);
        Assert.Equal("refresh", refresh[0]);
        Assert.Contains("--revision=5", refresh);
        Assert.DoesNotContain("--classic", refresh); // classic only applies to installation
    }
}
