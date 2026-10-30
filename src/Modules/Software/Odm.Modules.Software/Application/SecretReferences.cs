using System.Text.RegularExpressions;

namespace Odm.Modules.Software.Application;

/// <summary>Verweise der Form {{secret:NAME}} in Argumenten.</summary>
public static partial class SecretReferences
{
    public const string NamePattern = "^[A-Za-z0-9_.-]{1,64}$";

    [GeneratedRegex(@"\{\{secret:([A-Za-z0-9_.-]{1,64})\}\}")]
    private static partial Regex Reference();

    public static IReadOnlyList<string> Extract(string? text) =>
        string.IsNullOrEmpty(text)
            ? []
            : Reference().Matches(text).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();
}
