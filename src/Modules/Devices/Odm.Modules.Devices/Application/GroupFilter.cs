using System.Linq.Expressions;
using System.Reflection;
using Odm.Modules.Devices.Domain;

namespace Odm.Modules.Devices.Application;

/// <summary>
/// Übersetzt Gruppenregeln in einen Ausdruck, den EF Core in SQL umsetzt.
/// Textvergleiche sind unabhängig von Gross-/Kleinschreibung.
/// </summary>
public static class GroupFilter
{
    private static readonly MethodInfo ToLower = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
    private static readonly MethodInfo StartsWith = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;
    private static readonly MethodInfo EndsWith = typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!;
    private static readonly MethodInfo Contains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;

    // Wert in einem Objekt kapseln, damit EF ihn als Parameter (nicht als Literal) an die DB übergibt.
    private sealed class ValueBox(string value)
    {
        public string Value { get; } = value;
    }

    /// <summary>Gruppe ohne Regeln enthält keine Geräte.</summary>
    public static Expression<Func<Device, bool>> Build(GroupMatchMode mode, IReadOnlyCollection<GroupRule> rules)
    {
        if (rules.Count == 0)
        {
            return _ => false;
        }

        var device = Expression.Parameter(typeof(Device), "d");
        Expression? body = null;

        foreach (var rule in rules)
        {
            var predicate = BuildRule(device, rule);
            body = body is null
                ? predicate
                : mode == GroupMatchMode.All
                    ? Expression.AndAlso(body, predicate)
                    : Expression.OrElse(body, predicate);
        }

        return Expression.Lambda<Func<Device, bool>>(body!, device);
    }

    private static Expression BuildRule(ParameterExpression device, GroupRule rule)
    {
        if (rule.Field == GroupField.OsFamily)
        {
            var family = Enum.Parse<OsFamily>(rule.Value, ignoreCase: true);
            return Expression.Equal(
                Expression.Property(device, nameof(Device.OsFamily)),
                Expression.Constant(family));
        }

        var member = Expression.Property(device, PropertyName(rule.Field));
        var lowered = Expression.Call(member, ToLower);
        var value = Expression.Property(
            Expression.Constant(new ValueBox(rule.Value.ToLowerInvariant())),
            nameof(ValueBox.Value));

        return rule.Operator switch
        {
            MatchOperator.Is => Expression.Equal(lowered, value),
            MatchOperator.StartsWith => Expression.Call(lowered, StartsWith, value),
            MatchOperator.EndsWith => Expression.Call(lowered, EndsWith, value),
            MatchOperator.Contains => Expression.Call(lowered, Contains, value),
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule.Operator, null)
        };
    }

    private static string PropertyName(GroupField field) => field switch
    {
        GroupField.Name => nameof(Device.Name),
        GroupField.OsName => nameof(Device.OsName),
        GroupField.OsVersion => nameof(Device.OsVersion),
        GroupField.PrimaryUser => nameof(Device.PrimaryUser),
        GroupField.OrgUnit => nameof(Device.PrimaryUserOrgUnit),
        GroupField.CostCenter => nameof(Device.CostCenter),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
    };
}
