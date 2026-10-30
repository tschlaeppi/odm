using Odm.Modules.Devices.Application;
using Odm.Modules.Devices.Domain;

namespace Odm.Tests;

/// <summary>
/// Der Gruppenfilter wird für EF Core gebaut; hier wird derselbe Ausdruck im Speicher ausgewertet.
/// Alle Testgeräte haben die geprüften Felder gesetzt (im Speicher würde ein leeres Feld eine NullReferenceException
/// auslösen, in SQL ergibt es "keine Übereinstimmung").
/// </summary>
public class GroupFilterTests
{
    private static Device NewDevice(string name, OsFamily family, string osName, string osVersion, string orgUnit = "IT")
    {
        var device = Device.Register(name, family, osName, osVersion);
        device.UpdateAssignment("user", orgUnit, "KST-1");
        return device;
    }

    private static readonly Device Pc1 = NewDevice("PC-0001", OsFamily.Windows, "Windows 11 Pro", "24H2", "Finanz");
    private static readonly Device Pc2 = NewDevice("PC-0002", OsFamily.Windows, "Windows 10 Enterprise", "22H2");
    private static readonly Device Srv = NewDevice("SRV-WIN-01", OsFamily.Windows, "Windows Server 2022", "21H2");
    private static readonly Device Lx = NewDevice("lx-srv-01", OsFamily.Linux, "Ubuntu", "24.04");

    private static List<string> Names(GroupMatchMode mode, params GroupRule[] rules) =>
        new[] { Pc1, Pc2, Srv, Lx }
            .Where(GroupFilter.Build(mode, rules).Compile())
            .Select(d => d.Name)
            .ToList();

    [Fact]
    public void StartsWith_ignores_case()
    {
        var result = Names(GroupMatchMode.All, GroupRule.Create(GroupField.Name, MatchOperator.StartsWith, "pc-"));
        Assert.Equal(Arr("PC-0001", "PC-0002"), result);
    }

    [Fact]
    public void EndsWith_and_Contains_and_Is()
    {
        Assert.Equal(Arr("SRV-WIN-01", "lx-srv-01"),
            Names(GroupMatchMode.All, GroupRule.Create(GroupField.Name, MatchOperator.EndsWith, "-01")));
        Assert.Equal(Arr("SRV-WIN-01", "lx-srv-01"),
            Names(GroupMatchMode.All, GroupRule.Create(GroupField.Name, MatchOperator.Contains, "SRV")));
        Assert.Equal(Arr("PC-0002"),
            Names(GroupMatchMode.All, GroupRule.Create(GroupField.Name, MatchOperator.Is, "pc-0002")));
    }

    [Fact]
    public void All_requires_every_rule()
    {
        var result = Names(GroupMatchMode.All,
            GroupRule.Create(GroupField.OsFamily, MatchOperator.Is, "Windows"),
            GroupRule.Create(GroupField.OsName, MatchOperator.Contains, "windows 11"));
        Assert.Equal(Arr("PC-0001"), result);
    }

    [Fact]
    public void Any_requires_one_rule()
    {
        var result = Names(GroupMatchMode.Any,
            GroupRule.Create(GroupField.Name, MatchOperator.StartsWith, "SRV"),
            GroupRule.Create(GroupField.Name, MatchOperator.Contains, "-srv-"));
        Assert.Equal(Arr("SRV-WIN-01", "lx-srv-01"), result);
    }

    [Fact]
    public void OsFamily_rule_compares_the_enum()
    {
        Assert.Equal(Arr("lx-srv-01"), Names(GroupMatchMode.All, GroupRule.Create(GroupField.OsFamily, MatchOperator.Is, "Linux")));
    }

    [Fact]
    public void Organizational_unit_and_version_rules()
    {
        Assert.Equal(Arr("PC-0001"), Names(GroupMatchMode.All, GroupRule.Create(GroupField.OrgUnit, MatchOperator.Is, "finanz")));
        Assert.Equal(Arr("PC-0002"), Names(GroupMatchMode.All, GroupRule.Create(GroupField.OsVersion, MatchOperator.StartsWith, "22")));
    }

    [Fact]
    public void Group_without_rules_contains_no_devices()
    {
        Assert.Empty(Names(GroupMatchMode.All));
        Assert.Empty(Names(GroupMatchMode.Any));
    }
}
