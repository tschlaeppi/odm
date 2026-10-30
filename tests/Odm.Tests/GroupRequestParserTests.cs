using Odm.Modules.Devices.Application;
using Odm.Modules.Devices.Domain;

namespace Odm.Tests;

public class GroupRequestParserTests
{
    private static GroupRequest Request(string? name = "Gruppe", string? mode = "All", GroupRuleDto[]? rules = null) =>
        new(name, null, mode, (rules ?? []).ToList());

    [Fact]
    public void Valid_request_is_parsed_and_os_family_is_normalized()
    {
        var ok = GroupRequestParser.TryParse(
            Request(rules: [new GroupRuleDto("OsFamily", "Is", "linux")]), requireName: true, out var input, out var errors);

        Assert.True(ok);
        Assert.Empty(errors);
        Assert.Equal("Linux", input!.Rules[0].Value);
        Assert.Equal(GroupMatchMode.All, input.MatchMode);
    }

    [Fact]
    public void Name_is_required_unless_previewing()
    {
        Assert.False(GroupRequestParser.TryParse(Request(name: " "), requireName: true, out _, out var errors));
        Assert.True(errors.ContainsKey("name"));

        Assert.True(GroupRequestParser.TryParse(Request(name: null), requireName: false, out _, out _));
    }

    [Fact]
    public void OsFamily_rule_only_allows_Is_with_a_known_family()
    {
        Assert.False(GroupRequestParser.TryParse(
            Request(rules: [new GroupRuleDto("OsFamily", "Contains", "Windows")]), true, out _, out var e1));
        Assert.True(e1.ContainsKey("rules[0]"));

        Assert.False(GroupRequestParser.TryParse(
            Request(rules: [new GroupRuleDto("OsFamily", "Is", "BeOS")]), true, out _, out var e2));
        Assert.True(e2.ContainsKey("rules[0]"));
    }

    [Theory]
    [InlineData("Unbekannt", "Is", "x")]
    [InlineData("Name", "Matches", "x")]
    [InlineData("Name", "Is", "")]
    public void Invalid_rules_are_rejected(string field, string op, string value)
    {
        Assert.False(GroupRequestParser.TryParse(Request(rules: [new GroupRuleDto(field, op, value)]), true, out _, out var errors));
        Assert.True(errors.ContainsKey("rules[0]"));
    }

    [Fact]
    public void Invalid_match_mode_and_too_many_rules_are_rejected()
    {
        Assert.False(GroupRequestParser.TryParse(Request(mode: "Irgendwie"), true, out _, out var e1));
        Assert.True(e1.ContainsKey("matchMode"));

        var tooMany = Enumerable.Range(0, 21).Select(i => new GroupRuleDto("Name", "Contains", $"x{i}")).ToArray();
        Assert.False(GroupRequestParser.TryParse(Request(rules: tooMany), true, out _, out var e2));
        Assert.True(e2.ContainsKey("rules"));
    }
}
