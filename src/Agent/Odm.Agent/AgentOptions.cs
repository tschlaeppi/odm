namespace Odm.Agent;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Basis-URL des ODM-Servers, z. B. https://odm.example.org</summary>
    public string ServerUrl { get; set; } = "";

    /// <summary>One-time token from the web UI; only needed for the first registration.</summary>
    public string RegistrationToken { get; set; } = "";

    /// <summary>Tests only: allows http:// instead of https://.</summary>
    public bool AllowInsecureHttp { get; set; }

    /// <summary>Location of the credentials. Empty = default per operating system.</summary>
    public string StateDirectory { get; set; } = "";

    /// <summary>How often the inventory (hardware, installed programs) is sent. 0 = never.</summary>
    public int InventoryHours { get; set; } = 6;

    public string? Validate()
    {
        if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return "Agent:ServerUrl is missing or not a valid http(s) URL.";
        }

        if (uri.Scheme == Uri.UriSchemeHttp && !AllowInsecureHttp)
        {
            return "Agent:ServerUrl must use https (Agent:AllowInsecureHttp is for tests only).";
        }

        return null;
    }
}
