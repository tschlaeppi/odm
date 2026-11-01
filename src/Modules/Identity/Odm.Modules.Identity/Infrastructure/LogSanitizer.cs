namespace Odm.Modules.Identity.Infrastructure;

/// <summary>Makes values that come from outside (sign-in form, identity provider) safe to write to a log line.</summary>
internal static class LogSanitizer
{
    private const int MaxLength = 128;

    /// <summary>Removes line breaks and other control characters (log forging) and shortens very long values.</summary>
    public static string Clean(string? value)
    {
        var text = (value ?? "").Replace("\r", "").Replace("\n", "");
        text = new string(text.Where(c => !char.IsControl(c)).ToArray());
        return text.Length <= MaxLength ? text : text[..MaxLength] + "…";
    }
}
