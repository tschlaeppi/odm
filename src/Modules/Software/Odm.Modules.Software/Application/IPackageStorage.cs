namespace Odm.Modules.Software.Application;

public interface IPackageStorage
{
    Task EnsureBucketAsync(CancellationToken ct);

    /// <summary>Uploads a local file (a file so that the length is known and retries are possible).</summary>
    Task PutFileAsync(string key, string filePath, CancellationToken ct);

    Task<Stream> OpenReadAsync(string key, CancellationToken ct);

    Task DeleteAsync(string key, CancellationToken ct);
}
