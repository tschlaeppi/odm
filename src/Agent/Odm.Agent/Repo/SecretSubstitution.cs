using System.Text.RegularExpressions;

namespace Odm.Agent.Repo;

/// <summary>Replaces {{secret:NAME}} in arguments and removes secret values from output.</summary>
public static partial class SecretSubstitution
{
    [GeneratedRegex(@"\{\{secret:([A-Za-z0-9_.-]{1,64})\}\}")]
    private static partial Regex Reference();

    public static bool HasReferences(string? text) => text is not null && Reference().IsMatch(text);

    public static string Apply(string token, IReadOnlyDictionary<string, string> secrets) =>
        Reference().Replace(token, match =>
        {
            var name = match.Groups[1].Value;
            return secrets.TryGetValue(name, out var value)
                ? value
                : throw new InvalidOperationException($"The secret '{name}' is not defined.");
        });

    /// <summary>
    /// Installers occasionally print their command line. So that secret values do not end up in job results
    /// and thus in the database, they are masked before reporting.
    /// Very short values (under 3 characters) are not replaced because the output would become unreadable.
    /// </summary>
    public static string Redact(string? text, IEnumerable<string> secretValues)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        foreach (var value in secretValues.Where(v => v.Length >= 3).OrderByDescending(v => v.Length))
        {
            text = text.Replace(value, "***", StringComparison.Ordinal);
        }

        return text;
    }
}
