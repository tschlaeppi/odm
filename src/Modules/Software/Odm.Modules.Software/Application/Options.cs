namespace Odm.Modules.Software.Application;

public sealed class S3Options
{
    public const string SectionName = "Storage:S3";

    /// <summary>Leer = AWS. Für MinIO o. Ä. die URL des Dienstes angeben.</summary>
    public string ServiceUrl { get; set; } = "";

    public string Region { get; set; } = "us-east-1";
    public string Bucket { get; set; } = "odm-packages";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public bool ForcePathStyle { get; set; } = true;
}

public sealed class SecretsOptions
{
    public const string SectionName = "Secrets";

    /// <summary>Base64-codierter 256-Bit-Schlüssel (z. B. "openssl rand -base64 32").</summary>
    public string MasterKey { get; set; } = "";

    public static bool IsValid(string key)
    {
        try
        {
            return Convert.FromBase64String(key).Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed class SoftwareOptions
{
    public const string SectionName = "Software";

    /// <summary>Maximale Grösse eines hochgeladenen Pakets (S3 erlaubt in einem Upload höchstens 5 GB).</summary>
    public int MaxPackageMegabytes { get; set; } = 4096;

    /// <summary>Spätestens nach dieser Zeit wird der Soll-Zustand neu abgeglichen.</summary>
    public int ReconcileSeconds { get; set; } = 60;

    /// <summary>
    /// Automatisch aktualisierte Repo-Pakete (ohne feste Version) werden nach dieser Zeit erneut "installiert/aktualisiert".
    /// 0 = nie.
    /// </summary>
    public int RepoRefreshHours { get; set; } = 24;
}
