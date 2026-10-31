using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Odm.Modules.Agents.Application;
using Xunit;

namespace Odm.Tests;

public class AgentUpdatePlannerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("odm-upd").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private AgentPackageCatalog Catalog(params string[] files)
    {
        foreach (var file in files)
        {
            File.WriteAllText(Path.Combine(_dir, file), file);
        }

        return new AgentPackageCatalog(Options.Create(new AgentsOptions { PackagesPath = _dir }), new Env());
    }

    private static AgentUpdateOptions Latest(int percent = 100) =>
        new() { Mode = AgentUpdateMode.Latest, RolloutPercent = percent };

    [Fact]
    public void Off_never_offers()
    {
        var catalog = Catalog("odm-agent-0.5.0-linux-x64.tar.gz");
        Assert.Null(AgentUpdatePlanner.Plan(new AgentUpdateOptions(), catalog, "linux-x64", "0.4.0", Guid.NewGuid()));
    }

    [Fact]
    public void Latest_offers_only_newer_versions()
    {
        var catalog = Catalog("odm-agent-0.5.0-linux-x64.tar.gz", "odm-agent-0.4.0-linux-x64.tar.gz");
        var device = Guid.NewGuid();

        var offer = AgentUpdatePlanner.Plan(Latest(), catalog, "linux-x64", "0.4.0", device);
        Assert.NotNull(offer);
        Assert.Equal("0.5.0", offer.Version);
        Assert.Equal("api/agent/download/linux-x64?version=0.5.0", offer.Url);
        Assert.Equal(64, offer.Sha256.Length);

        Assert.Null(AgentUpdatePlanner.Plan(Latest(), catalog, "linux-x64", "0.5.0", device));
        Assert.Null(AgentUpdatePlanner.Plan(Latest(), catalog, "linux-x64", "0.6.0", device));
    }

    [Fact]
    public void Pinned_allows_downgrade_but_only_to_existing_packages()
    {
        var catalog = Catalog("odm-agent-0.5.0-win-x64.zip", "odm-agent-0.4.0-win-x64.zip");
        var options = new AgentUpdateOptions { Mode = AgentUpdateMode.Pinned, TargetVersion = "0.4.0" };
        var device = Guid.NewGuid();

        Assert.Equal("0.4.0", AgentUpdatePlanner.Plan(options, catalog, "win-x64", "0.5.0", device)!.Version);
        Assert.Null(AgentUpdatePlanner.Plan(options, catalog, "win-x64", "0.4.0", device));

        options.TargetVersion = "9.9.9";
        Assert.Null(AgentUpdatePlanner.Plan(options, catalog, "win-x64", "0.4.0", device));
        options.TargetVersion = "../evil";
        Assert.Null(AgentUpdatePlanner.Plan(options, catalog, "win-x64", "0.4.0", device));
    }

    [Theory]
    [InlineData(null, "0.4.0")]
    [InlineData("linux-x64", null)]
    [InlineData("osx-arm64", "0.4.0")]
    [InlineData("linux-x64", "abc")]
    [InlineData("linux-arm64", "0.4.0")] // no file for this platform
    public void Invalid_or_unknown_input_yields_no_offer(string? platform, string? version)
    {
        var catalog = Catalog("odm-agent-0.5.0-linux-x64.tar.gz");
        Assert.Null(AgentUpdatePlanner.Plan(Latest(), catalog, platform, version, Guid.NewGuid()));
    }

    [Fact]
    public void Rollout_percent_is_stable_and_roughly_proportional()
    {
        var catalog = Catalog("odm-agent-0.5.0-linux-x64.tar.gz");
        var devices = Enumerable.Range(0, 2000).Select(_ => Guid.NewGuid()).ToList();

        var first = devices.Select(d => AgentUpdatePlanner.Plan(Latest(30), catalog, "linux-x64", "0.4.0", d) is not null).ToList();
        var second = devices.Select(d => AgentUpdatePlanner.Plan(Latest(30), catalog, "linux-x64", "0.4.0", d) is not null).ToList();
        Assert.Equal(first, second);

        var share = first.Count(x => x) / 2000.0;
        Assert.InRange(share, 0.24, 0.36);

        Assert.All(devices, d => Assert.NotNull(AgentUpdatePlanner.Plan(Latest(100), catalog, "linux-x64", "0.4.0", d)));
        Assert.All(devices, d => Assert.Null(AgentUpdatePlanner.Plan(Latest(0), catalog, "linux-x64", "0.4.0", d)));
    }

    [Fact]
    public void Devices_in_a_lower_rollout_stay_in_a_higher_one()
    {
        var catalog = Catalog("odm-agent-0.5.0-linux-x64.tar.gz");
        foreach (var d in Enumerable.Range(0, 300).Select(_ => Guid.NewGuid()))
        {
            if (AgentUpdatePlanner.Plan(Latest(10), catalog, "linux-x64", "0.4.0", d) is not null)
            {
                Assert.NotNull(AgentUpdatePlanner.Plan(Latest(50), catalog, "linux-x64", "0.4.0", d));
            }
        }
    }

    private sealed class Env : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
