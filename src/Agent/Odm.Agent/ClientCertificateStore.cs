using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Odm.Agent;

/// <summary>
/// Schlüsselpaar und Client-Zertifikat des Agents (mTLS). Der private Schlüssel (ECDSA P-256) wird auf dem Gerät erzeugt und
/// verlässt es nie; der Server erhält nur eine Zertifikatsanforderung. Beides liegt im geschützten Zustandsverzeichnis.
/// </summary>
public sealed class ClientCertificateStore(AgentStateStore store, ILogger<ClientCertificateStore> logger)
{
    private readonly object _gate = new();
    private X509Certificate2? _current;
    private bool _loaded;

    private string KeyFile => Path.Combine(store.StateDirectory, "agent.key");
    private string CertFile => Path.Combine(store.StateDirectory, "agent.crt");

    /// <summary>Das aktuelle Client-Zertifikat samt Schlüssel oder <c>null</c>, wenn noch keines vorhanden ist.</summary>
    public X509Certificate2? Current
    {
        get
        {
            lock (_gate)
            {
                if (!_loaded)
                {
                    _current = Load();
                    _loaded = true;
                }

                return _current;
            }
        }
    }

    /// <summary>Neues Schlüsselpaar und CSR (PEM). Der Schlüssel wird erst mit <see cref="Save"/> übernommen.</summary>
    public (ECDsa Key, string CsrPem) CreateRequest()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=odm-agent", key, HashAlgorithmName.SHA256);
        return (key, request.CreateSigningRequestPem());
    }

    /// <summary>Speichert Schlüssel und das vom Server ausgestellte Zertifikat und verwendet sie ab sofort.</summary>
    public void Save(ECDsa key, string certificatePem)
    {
        // Prüfen, dass Zertifikat und Schlüssel zusammengehören, bevor die alten Dateien ersetzt werden.
        using var parsed = X509Certificate2.CreateFromPem(certificatePem, key.ExportPkcs8PrivateKeyPem());

        System.IO.Directory.CreateDirectory(store.StateDirectory);
        WriteSecret(KeyFile, key.ExportPkcs8PrivateKeyPem());
        WriteSecret(CertFile, certificatePem);

        lock (_gate)
        {
            _current?.Dispose();
            _current = Load();
            _loaded = true;
        }

        logger.LogInformation("Client certificate stored (valid until {NotAfter:u}).", parsed.NotAfter);
    }

    private X509Certificate2? Load()
    {
        if (!File.Exists(KeyFile) || !File.Exists(CertFile))
        {
            return null;
        }

        try
        {
            using var pem = X509Certificate2.CreateFromPem(File.ReadAllText(CertFile), File.ReadAllText(KeyFile));
            // Unter Windows kann SChannel kurzlebige Schlüssel nicht verwenden; der Umweg über PKCS#12 macht sie nutzbar.
            return X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pkcs12), null);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The client certificate could not be loaded.");
            return null;
        }
    }

    private static void WriteSecret(string path, string content)
    {
        var temp = path + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var stream = new FileStream(temp, options))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(content);
        }

        File.Move(temp, path, overwrite: true);
    }
}
