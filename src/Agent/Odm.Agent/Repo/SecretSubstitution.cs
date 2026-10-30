using System.Text.RegularExpressions;

namespace Odm.Agent.Repo;

/// <summary>Ersetzt {{secret:NAME}} in Argumenten und entfernt Secret-Werte aus Ausgaben.</summary>
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
                : throw new InvalidOperationException($"Das Secret '{name}' ist nicht definiert.");
        });

    /// <summary>
    /// Installer geben ihre Befehlszeile gelegentlich aus. Damit Secret-Werte nicht in Auftragsergebnissen
    /// und damit in der Datenbank landen, werden sie vor dem Melden unkenntlich gemacht.
    /// Sehr kurze Werte (unter 3 Zeichen) werden nicht ersetzt, weil sonst die Ausgabe unlesbar würde.
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
