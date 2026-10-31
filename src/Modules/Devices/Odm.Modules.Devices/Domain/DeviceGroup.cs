using Odm.SharedKernel;

namespace Odm.Modules.Devices.Domain;

/// <summary>Device property that a group rule targets.</summary>
public enum GroupField
{
    Name = 0,
    OsFamily = 1,
    OsName = 2,
    OsVersion = 3,
    PrimaryUser = 4,
    OrgUnit = 5,
    CostCenter = 6
}

public enum MatchOperator
{
    Is = 0,
    StartsWith = 1,
    EndsWith = 2,
    Contains = 3
}

public enum GroupMatchMode
{
    /// <summary>All rules must match.</summary>
    All = 0,

    /// <summary>At least one rule must match.</summary>
    Any = 1
}

/// <summary>Single rule of a group (value object, owned by the group).</summary>
public sealed class GroupRule
{
    public GroupField Field { get; private set; }
    public MatchOperator Operator { get; private set; }
    public string Value { get; private set; } = "";

    private GroupRule() { }

    public static GroupRule Create(GroupField field, MatchOperator op, string value) =>
        new() { Field = field, Operator = op, Value = value.Trim() };
}

/// <summary>Dynamic device group: members result from the rules.</summary>
public sealed class DeviceGroup : Entity
{
    private readonly List<GroupRule> _rules = [];

    public string Name { get; private set; } = "";
    public string? Description { get; private set; }
    public GroupMatchMode MatchMode { get; private set; }
    public IReadOnlyList<GroupRule> Rules => _rules;

    private DeviceGroup() { }

    public static DeviceGroup Create(string name, string? description, GroupMatchMode mode, IEnumerable<GroupRule> rules)
    {
        var group = new DeviceGroup();
        group.Update(name, description, mode, rules);
        return group;
    }

    public void Update(string name, string? description, GroupMatchMode mode, IEnumerable<GroupRule> rules)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        MatchMode = mode;
        _rules.Clear();
        _rules.AddRange(rules);
    }
}
