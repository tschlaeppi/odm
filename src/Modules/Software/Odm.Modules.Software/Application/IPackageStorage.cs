namespace Odm.Modules.Software.Application;

public interface IPackageStorage
{
    Task EnsureBucketAsync(CancellationToken ct);

    /// <summary>Lädt eine lokale Datei hoch (Datei, damit die Länge bekannt ist und Wiederholungen möglich sind).</summary>
    Task PutFileAsync(string key, string filePath, CancellationToken ct);

    Task<Stream> OpenReadAsync(string key, CancellationToken ct);

    Task DeleteAsync(string key, CancellationToken ct);
}
