using Odm.Modules.Devices.Domain;

namespace Odm.Modules.Devices.Application;

public sealed record GroupRuleDto(string Field, string Operator, string Value);

public sealed record GroupRequest(string? Name, string? Description, string? MatchMode, List<GroupRuleDto>? Rules);

public sealed record GroupDto(
    Guid Id,
    string Name,
    string? Description,
    string MatchMode,
    IReadOnlyList<GroupRuleDto> Rules,
    int DeviceCount)
{
    public static GroupDto From(DeviceGroup g, int deviceCount) => new(
        g.Id,
        g.Name,
        g.Description,
        g.MatchMode.ToString(),
        g.Rules.Select(r => new GroupRuleDto(r.Field.ToString(), r.Operator.ToString(), r.Value)).ToList(),
        deviceCount);
}

/// <summary>Validated input from a <see cref="GroupRequest"/>.</summary>
public sealed record GroupInput(string Name, string? Description, GroupMatchMode MatchMode, IReadOnlyList<GroupRule> Rules);

public static class GroupRequestParser
{
    private const int MaxRules = 20;

    public static bool TryParse(
        GroupRequest request, bool requireName, out GroupInput? input, out Dictionary<string, string[]> errors)
    {
        errors = [];
        input = null;

        var name = request.Name?.Trim() ?? "";
        if (requireName && name.Length == 0)
        {
            errors["name"] = ["Name is required."];
        }
        else if (name.Length > 128)
        {
            errors["name"] = ["Name must be at most 128 characters."];
        }

        if (request.Description is { Length: > 1024 })
        {
            errors["description"] = ["Description must be at most 1024 characters."];
        }

        if (!Enum.TryParse<GroupMatchMode>(request.MatchMode, true, out var mode) || !Enum.IsDefined(mode))
        {
            errors["matchMode"] = ["Invalid match mode."];
        }

        var rules = new List<GroupRule>();
        var dtos = request.Rules ?? [];
        if (dtos.Count > MaxRules)
        {
            errors["rules"] = [$"At most {MaxRules} rules are allowed."];
        }
        else
        {
            for (var i = 0; i < dtos.Count; i++)
            {
                var error = ParseRule(dtos[i], out var rule);
                if (error is not null)
                {
                    errors[$"rules[{i}]"] = [error];
                }
                else
                {
                    rules.Add(rule!);
                }
            }
        }

        if (errors.Count > 0)
        {
            return false;
        }

        input = new GroupInput(name, request.Description, mode, rules);
        return true;
    }

    private static string? ParseRule(GroupRuleDto dto, out GroupRule? rule)
    {
        rule = null;

        if (!Enum.TryParse<GroupField>(dto.Field, true, out var field) || !Enum.IsDefined(field))
        {
            return "Invalid field.";
        }

        if (!Enum.TryParse<MatchOperator>(dto.Operator, true, out var op) || !Enum.IsDefined(op))
        {
            return "Invalid operator.";
        }

        var value = dto.Value?.Trim() ?? "";
        if (value.Length is 0 or > 256)
        {
            return "Value is required (at most 256 characters).";
        }

        if (field == GroupField.OsFamily)
        {
            if (op != MatchOperator.Is || !Enum.TryParse<OsFamily>(value, true, out var family) || !Enum.IsDefined(family))
            {
                return "Operating system family: only 'Is' with Windows or Linux.";
            }

            value = family.ToString();
        }

        rule = GroupRule.Create(field, op, value);
        return null;
    }
}
