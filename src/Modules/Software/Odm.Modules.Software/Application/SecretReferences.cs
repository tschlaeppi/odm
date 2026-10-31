using System.Text.RegularExpressions;

namespace Odm.Modules.Software.Application;

/// <summary>References of the form {{secret:NAME}} in arguments.</summary>
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
