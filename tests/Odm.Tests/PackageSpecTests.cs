using Odm.Modules.Software.Application;
using Odm.Modules.Software.Domain;

namespace Odm.Tests;

public class PackageSpecTests
{
    private static readonly IReadOnlySet<string> NoSecrets = new HashSet<string>();

    private static PackageSpecDto Dto(
        string? id = null, string? version = null, string? scope = null, string? source = null,
        string? remote = null, string? channel = null, bool classic = false, string? args = null) =>
        new(id, version, scope, source, remote, channel, classic, args);

    [Fact]
    public void Winget_defaults_to_machine_scope()
    {
        var ok = PackageSpecValidator.TryValidate(PackageKind.Winget, Dto(id: "Mozilla.Firefox"), NoSecrets, out var spec, out var errors);

        Assert.True(ok);
        Assert.Empty(errors);
        Assert.Equal("machine", spec.Scope);
        Assert.Null(spec.Version);
        Assert.True(spec.AutoUpdate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("--force")]      // must not be interpreted as an option of the package manager
    [InlineData("-y")]
    [InlineData("name with space")]
    [InlineData("name;rm -rf")]
    public void Repo_package_needs_a_safe_identifier(string? identifier)
    {
        var ok = PackageSpecValidator.TryValidate(PackageKind.Apt, Dto(id: identifier), NoSecrets, out _, out var errors);

        Assert.False(ok);
        Assert.True(errors.ContainsKey("identifier"));
    }

    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("2:1.0-1ubuntu1~22.04", true)] // APT version with epoch and tilde
    [InlineData("-1", false)]
    [InlineData("1 2", false)]
    public void Version_format(string version, bool valid)
    {
        var ok = PackageSpecValidator.TryValidate(PackageKind.Apt, Dto(id: "nginx", version: version), NoSecrets, out var spec, out var errors);

        Assert.Equal(valid, ok);
        if (valid) Assert.False(spec.AutoUpdate);
        else Assert.True(errors.ContainsKey("version"));
    }

    [Fact]
    public void Scope_must_match_the_package_manager()
    {
        Assert.False(PackageSpecValidator.TryValidate(PackageKind.Winget, Dto(id: "x", scope: "root"), NoSecrets, out _, out var e1));
        Assert.True(e1.ContainsKey("scope"));

        Assert.True(PackageSpecValidator.TryValidate(PackageKind.Flatpak, Dto(id: "org.x.y"), NoSecrets, out var flatpak, out _));
        Assert.Equal("system", flatpak.Scope);
        Assert.Equal("flathub", flatpak.Remote);
    }

    [Fact]
    public void Snap_defaults_and_classic_only_for_snap()
    {
        Assert.True(PackageSpecValidator.TryValidate(PackageKind.Snap, Dto(id: "code", classic: true), NoSecrets, out var snap, out _));
        Assert.Equal("stable", snap.Channel);
        Assert.True(snap.Classic);

        Assert.True(PackageSpecValidator.TryValidate(PackageKind.Apt, Dto(id: "nginx", classic: true), NoSecrets, out var apt, out _));
        Assert.False(apt.Classic);
    }

    [Fact]
    public void Irrelevant_fields_are_dropped_so_equal_settings_give_equal_json()
    {
        PackageSpecValidator.TryValidate(PackageKind.Apt, Dto(id: "nginx", scope: "user", source: "x", remote: "y", channel: "z"), NoSecrets, out var a, out _);
        PackageSpecValidator.TryValidate(PackageKind.Apt, Dto(id: "nginx"), NoSecrets, out var b, out _);

        Assert.Equal(a.ToJson(), b.ToJson());
    }

    [Fact]
    public void Psadt_ignores_identifier_and_version_but_keeps_arguments()
    {
        var ok = PackageSpecValidator.TryValidate(PackageKind.Psadt, Dto(id: "x", version: "1", args: "-Silent"), NoSecrets, out var spec, out _);

        Assert.True(ok);
        Assert.Null(spec.Identifier);
        Assert.Null(spec.Version);
        Assert.Equal("-Silent", spec.Arguments);
    }

    [Fact]
    public void Arguments_may_only_reference_existing_secrets()
    {
        var known = new HashSet<string> { "LIZENZ" };

        Assert.True(PackageSpecValidator.TryValidate(PackageKind.Winget, Dto(id: "x", args: "--key {{secret:LIZENZ}}"), known, out _, out _));

        Assert.False(PackageSpecValidator.TryValidate(PackageKind.Winget, Dto(id: "x", args: "--key {{secret:FEHLT}}"), known, out _, out var errors));
        Assert.Contains("FEHLT", errors["arguments"][0]);
    }

    [Fact]
    public void Arguments_have_a_length_limit()
    {
        var tooLong = new string('a', PackageSpecValidator.MaxArgumentsLength + 1);

        Assert.False(PackageSpecValidator.TryValidate(PackageKind.Apt, Dto(id: "x", args: tooLong), NoSecrets, out _, out var errors));
        Assert.True(errors.ContainsKey("arguments"));
    }

    [Fact]
    public void Json_roundtrip_and_tolerant_parsing()
    {
        var spec = new PackageSpec("nginx", "1.2", null, null, null, null, false, "-x");

        Assert.Equal(spec, PackageSpec.Parse(spec.ToJson()));
        Assert.Equal(PackageSpec.Empty, PackageSpec.Parse(null));
        Assert.Equal(PackageSpec.Empty, PackageSpec.Parse("kein json"));
    }

    [Theory]
    [InlineData("a {{secret:LIZENZ}} b {{secret:A.b-1}} {{secret:LIZENZ}}", new[] { "LIZENZ", "A.b-1" })]
    [InlineData("kein Verweis", new string[0])]
    [InlineData("{{secret:mit Leerzeichen}}", new string[0])]
    [InlineData("{secret:EINFACH}", new string[0])]
    public void Secret_references_are_extracted_once_and_in_order(string text, string[] expected) =>
        Assert.Equal(expected, SecretReferences.Extract(text));

    [Theory]
    [InlineData(null, null)]
    [InlineData("Windows", null)]
    [InlineData("linux", "Linux")]
    public void Psadt_platform_is_normalized(string? requested, string? expected)
    {
        var dto = Dto() with { Platform = requested };

        Assert.True(PackageSpecValidator.TryValidate(PackageKind.Psadt, dto, NoSecrets, out var spec, out _));
        Assert.Equal(expected, spec.Platform);
    }

    [Fact]
    public void Unknown_platform_is_rejected_and_other_kinds_ignore_it()
    {
        var dto = Dto() with { Platform = "Mac" };
        Assert.False(PackageSpecValidator.TryValidate(PackageKind.Psadt, dto, NoSecrets, out _, out var errors));
        Assert.True(errors.ContainsKey("platform"));

        var apt = Dto(id: "nginx") with { Platform = "Linux" };
        Assert.True(PackageSpecValidator.TryValidate(PackageKind.Apt, apt, NoSecrets, out var spec, out _));
        Assert.Null(spec.Platform);
    }

    [Fact]
    public void Spec_without_platform_serializes_without_the_field()
    {
        // Existing specs keep their JSON, so no spurious new revisions are created.
        Assert.DoesNotContain("platform", PackageSpec.Empty.ToJson());
        Assert.Contains("\"platform\":\"Linux\"", (PackageSpec.Empty with { Platform = "Linux" }).ToJson());
    }
}
