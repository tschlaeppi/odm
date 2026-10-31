namespace Odm.Modules.Software.Application;

public sealed class S3Options
{
    public const string SectionName = "Storage:S3";

    /// <summary>Empty = AWS. For MinIO or similar, give the URL of the service.</summary>
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

    /// <summary>Base64-encoded 256-bit key (e.g. "openssl rand -base64 32").</summary>
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

    /// <summary>Maximum size of an uploaded package (S3 allows at most 5 GB in one upload).</summary>
    public int MaxPackageMegabytes { get; set; } = 4096;

    /// <summary>The desired state is reconciled again after this time at the latest.</summary>
    public int ReconcileSeconds { get; set; } = 60;

    /// <summary>
    /// Automatically updated repo packages (without a pinned version) are "installed/updated" again after this time.
    /// 0 = nie.
    /// </summary>
    public int RepoRefreshHours { get; set; } = 24;
}
