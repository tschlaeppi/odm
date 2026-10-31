using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Odm.Agent;

/// <summary>
/// Key pair and client certificate of the agent (mTLS). The private key (ECDSA P-256) is created on the device and
/// never leaves it; the server receives only a certificate request. Both live in the protected state directory.
/// </summary>
public sealed class ClientCertificateStore(AgentStateStore store, ILogger<ClientCertificateStore> logger)
{
    private readonly object _gate = new();
    private X509Certificate2? _current;
    private bool _loaded;

    private string KeyFile => Path.Combine(store.StateDirectory, "agent.key");
    private string CertFile => Path.Combine(store.StateDirectory, "agent.crt");

    /// <summary>The current client certificate including its key, or <c>null</c> if there is none yet.</summary>
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

    /// <summary>New key pair and CSR (PEM). The key is only adopted with <see cref="Save"/>.</summary>
    public (ECDsa Key, string CsrPem) CreateRequest()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=odm-agent", key, HashAlgorithmName.SHA256);
        return (key, request.CreateSigningRequestPem());
    }

    /// <summary>Stores the key and the certificate issued by the server and uses them from now on.</summary>
    public void Save(ECDsa key, string certificatePem)
    {
        // Check that certificate and key belong together before the old files are replaced.
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
            // On Windows SChannel cannot use ephemeral keys; going through PKCS#12 makes them usable.
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
