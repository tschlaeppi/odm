using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Odm.Modules.Agents.Application;
using Xunit;

namespace Odm.Tests;

public class AgentInstallTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("odm-pkgs").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private AgentPackageCatalog Catalog() => new(
        Options.Create(new AgentsOptions { PackagesPath = _dir }),
        new FakeEnvironment());

    [Theory]
    [InlineData("odm-agent-0.4.0-win-x64.zip", "0.4.0", "win-x64")]
    [InlineData("odm-agent-1.2.3-linux-arm64.tar.gz", "1.2.3", "linux-arm64")]
    [InlineData("odm-agent-1.0.0-rc.1-linux-x64.tar.gz", "1.0.0-rc.1", "linux-x64")]
    public void File_names_are_parsed(string name, string version, string platform)
    {
        Assert.Equal((version, platform), AgentPackageCatalog.ParseFileName(name));
    }

    [Theory]
    [InlineData("odm-agent-0.4.0-win-x64.tar.gz")]
    [InlineData("odm-agent-0.4.0-linux-x64.zip")]
    [InlineData("odm-agent-0.4.0-osx-arm64.tar.gz")]
    [InlineData("../odm-agent-0.4.0-win-x64.zip")]
    [InlineData("odm-agent-latest-win-x64.zip")]
    public void Unexpected_file_names_are_ignored(string name)
    {
        Assert.Null(AgentPackageCatalog.ParseFileName(name));
    }

    [Fact]
    public void Versions_compare_semantically_and_releases_beat_prereleases()
    {
        Assert.True(AgentPackageCatalog.CompareVersions("0.10.0", "0.9.0") > 0);
        Assert.True(AgentPackageCatalog.CompareVersions("1.0.0", "1.0.0-rc.1") > 0);
        Assert.Equal(0, AgentPackageCatalog.CompareVersions("1.2.3", "1.2.3"));
    }

    [Fact]
    public void Catalog_picks_highest_version_per_platform_and_hashes_the_file()
    {
        File.WriteAllText(Path.Combine(_dir, "odm-agent-0.3.0-win-x64.zip"), "old");
        File.WriteAllText(Path.Combine(_dir, "odm-agent-0.10.0-win-x64.zip"), "abc");
        File.WriteAllText(Path.Combine(_dir, "odm-agent-0.4.0-linux-x64.tar.gz"), "x");
        File.WriteAllText(Path.Combine(_dir, "readme.txt"), "ignored");

        var packages = Catalog().List();

        Assert.Equal(["win-x64", "linux-x64"], packages.Select(p => p.Platform));
        var win = packages.Single(p => p.Platform == "win-x64");
        Assert.Equal("0.10.0", win.Version);
        // SHA-256("abc")
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", win.Sha256);
    }

    [Fact]
    public void Missing_directory_yields_no_packages()
    {
        var catalog = new AgentPackageCatalog(
            Options.Create(new AgentsOptions { PackagesPath = Path.Combine(_dir, "nope") }), new FakeEnvironment());
        Assert.Empty(catalog.List());
    }

    [Theory]
    [InlineData("https://odm.example.com/", "https://odm.example.com")]
    [InlineData("http://localhost:8080", "http://localhost:8080")]
    [InlineData("https://odm.example.com/odm", "https://odm.example.com/odm")]
    public void Safe_base_urls_are_accepted(string input, string expected)
    {
        Assert.Equal(expected, InstallScriptBuilder.NormalizeBaseUrl(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://x")]
    [InlineData("https://x'; rm -rf / #")]
    [InlineData("https://user:pw@x")]
    [InlineData("https://x/?a=b")]
    [InlineData("https://x\"y")]
    public void Unsafe_base_urls_are_rejected(string input)
    {
        Assert.Null(InstallScriptBuilder.NormalizeBaseUrl(input));
    }

    [Fact]
    public void Scripts_contain_server_and_checksums_and_no_placeholders()
    {
        File.WriteAllText(Path.Combine(_dir, "odm-agent-0.4.0-win-x64.zip"), "abc");
        File.WriteAllText(Path.Combine(_dir, "odm-agent-0.4.0-linux-x64.tar.gz"), "abc");
        var packages = Catalog().List();

        foreach (var template in new[] { "install.sh", "install.ps1" })
        {
            var script = InstallScriptBuilder.Build(template, "https://odm.example.com", packages);
            Assert.Contains("https://odm.example.com", script);
            Assert.Contains("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", script);
            Assert.DoesNotContain("@@", script);
            Assert.DoesNotContain("\r", script);
        }
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
