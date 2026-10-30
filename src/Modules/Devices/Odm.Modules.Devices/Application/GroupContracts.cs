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

/// <summary>Validierte Eingabe aus einer <see cref="GroupRequest"/>.</summary>
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
            errors["name"] = ["Name ist erforderlich."];
        }
        else if (name.Length > 128)
        {
            errors["name"] = ["Name darf höchstens 128 Zeichen lang sein."];
        }

        if (request.Description is { Length: > 1024 })
        {
            errors["description"] = ["Beschreibung darf höchstens 1024 Zeichen lang sein."];
        }

        if (!Enum.TryParse<GroupMatchMode>(request.MatchMode, true, out var mode) || !Enum.IsDefined(mode))
        {
            errors["matchMode"] = ["Ungültiger Abgleichmodus."];
        }

        var rules = new List<GroupRule>();
        var dtos = request.Rules ?? [];
        if (dtos.Count > MaxRules)
        {
            errors["rules"] = [$"Höchstens {MaxRules} Regeln erlaubt."];
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
            return "Ungültiges Feld.";
        }

        if (!Enum.TryParse<MatchOperator>(dto.Operator, true, out var op) || !Enum.IsDefined(op))
        {
            return "Ungültiger Operator.";
        }

        var value = dto.Value?.Trim() ?? "";
        if (value.Length is 0 or > 256)
        {
            return "Wert ist erforderlich (höchstens 256 Zeichen).";
        }

        if (field == GroupField.OsFamily)
        {
            if (op != MatchOperator.Is || !Enum.TryParse<OsFamily>(value, true, out var family) || !Enum.IsDefined(family))
            {
                return "Betriebssystemfamilie: nur 'Is' mit Windows oder Linux.";
            }

            value = family.ToString();
        }

        rule = GroupRule.Create(field, op, value);
        return null;
    }
}
