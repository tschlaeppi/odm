using System.Reflection;
using System.Text.RegularExpressions;

namespace Odm.Modules.Agents.Application;

/// <summary>Creates the convenience scripts from the embedded templates with server address and checksums.</summary>
public static partial class InstallScriptBuilder
{
    [GeneratedRegex(@"^https?://[A-Za-z0-9.\-\[\]:]+(/[A-Za-z0-9._~\-/]*)?$")]
    private static partial Regex SafeBaseUrl();

    /// <summary>
    /// Validates the server address and returns it without a trailing slash. The address is embedded in scripts,
    /// so only harmless characters are allowed (no quotation mark, whitespace, query, user info).
    /// </summary>
    public static string? NormalizeBaseUrl(string? value)
    {
        var url = value?.Trim().TrimEnd('/');
        return !string.IsNullOrEmpty(url) && url.Length <= 200 && SafeBaseUrl().IsMatch(url) ? url : null;
    }

    public static string Build(string template, string baseUrl, IEnumerable<AgentPackage> packages)
    {
        var script = LoadTemplate(template).Replace("@@SERVER@@", baseUrl, StringComparison.Ordinal);
        var byPlatform = packages.ToDictionary(p => p.Platform);
        foreach (var platform in AgentPackageCatalog.Platforms)
        {
            var key = platform.ToUpperInvariant().Replace('-', '_');
            script = script
                .Replace($"@@VERSION_{key}@@", byPlatform.TryGetValue(platform, out var p) ? p.Version : "", StringComparison.Ordinal)
                .Replace($"@@SHA_{key}@@", p?.Sha256 ?? "", StringComparison.Ordinal);
        }

        return script;
    }

    private static string LoadTemplate(string name)
    {
        var assembly = typeof(InstallScriptBuilder).Assembly;
        using var stream = assembly.GetManifestResourceStream($"Odm.Install.{name}")
                           ?? throw new InvalidOperationException($"Vorlage {name} fehlt.");
        using var reader = new StreamReader(stream);
        // Always deliver scripts with LF (sh does not tolerate CRLF).
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
