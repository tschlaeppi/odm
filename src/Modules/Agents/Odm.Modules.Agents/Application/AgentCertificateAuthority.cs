using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Odm.Modules.Agents.Application;

/// <summary>Ein ausgestelltes Agent-Zertifikat.</summary>
public sealed record IssuedCertificate(string CertificatePem, string Thumbprint, DateTimeOffset NotAfter);

/// <summary>
/// Own CA for agent certificates (ECDSA P-256). The agent creates its key pair itself and only sends a
/// certificate signing request (CSR); the server takes nothing but the public key from it.
/// Subject, extensions and validity are decided by the server alone. The subject (CN) is the agent ID.
/// </summary>
public sealed class AgentCertificateAuthority(IOptions<AgentsOptions> options, IHostEnvironment environment) : IDisposable
{
    private const string EcPublicKeyOid = "1.2.840.10045.2.1";
    private const string NistP256Oid = "1.2.840.10045.3.1.7";
    private const string ClientAuthOid = "1.3.6.1.5.5.7.3.2";

    private readonly object _gate = new();
    private X509Certificate2? _ca;

    public AgentMtlsMode Mode => options.Value.Mtls.Mode;

    public bool Enabled => Mode != AgentMtlsMode.Off;

    private string Directory
    {
        get
        {
            var path = options.Value.Mtls.CaDirectory;
            return Path.IsPathRooted(path) ? path : Path.Combine(environment.ContentRootPath, path);
        }
    }

    private X509Certificate2 Ca
    {
        get
        {
            lock (_gate)
            {
                return _ca ??= LoadOrCreate();
            }
        }
    }

    /// <summary>The public CA certificate (PEM) for the agents.</summary>
    public string CaCertificatePem => Ca.ExportCertificatePem();

    private X509Certificate2 LoadOrCreate()
    {
        var certFile = Path.Combine(Directory, "ca.crt");
        var keyFile = Path.Combine(Directory, "ca.key");

        if (!File.Exists(certFile) || !File.Exists(keyFile))
        {
            System.IO.Directory.CreateDirectory(Directory);
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=ODM Agent CA", key, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

            var now = DateTimeOffset.UtcNow;
            using var created = request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(10));

            // Key first and readable only by the owner; "CreateNew" prevents instances starting in parallel from overwriting each other.
            try
            {
                WriteNew(keyFile, key.ExportPkcs8PrivateKeyPem(), secret: true);
                WriteNew(certFile, created.ExportCertificatePem(), secret: false);
            }
            catch (IOException) when (File.Exists(keyFile))
            {
                // Another instance was faster; its files are loaded below.
                for (var i = 0; i < 20 && !File.Exists(certFile); i++)
                {
                    Thread.Sleep(100);
                }
            }
        }

        return X509Certificate2.CreateFromPem(File.ReadAllText(certFile), File.ReadAllText(keyFile));
    }

    private static void WriteNew(string path, string content, bool secret)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = secret
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite
                : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        }

        using var stream = new FileStream(path, options);
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    /// <summary>Issues a client certificate for the agent from a CSR. Throws <see cref="CryptographicException"/> for an invalid CSR.</summary>
    public IssuedCertificate Issue(string csrPem, Guid agentId, DateTimeOffset now)
    {
        var csr = CertificateRequest.LoadSigningRequestPem(csrPem, HashAlgorithmName.SHA256);

        if (csr.PublicKey.Oid.Value != EcPublicKeyOid)
        {
            throw new CryptographicException("Only ECDSA keys (P-256) are supported.");
        }

        using (var publicKey = csr.PublicKey.GetECDsaPublicKey()
                               ?? throw new CryptographicException("Invalid public key."))
        {
            if (publicKey.ExportParameters(false).Curve.Oid.Value != NistP256Oid)
            {
                throw new CryptographicException("Only the P-256 curve is supported.");
            }
        }

        var ca = Ca;
        var request = new CertificateRequest(
            new X500DistinguishedName($"CN={agentId:D}"), csr.PublicKey, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid(ClientAuthOid, "Client Authentication")], false));

        var notBefore = now.AddMinutes(-5);
        if (notBefore < ca.NotBefore)
        {
            notBefore = new DateTimeOffset(ca.NotBefore.ToUniversalTime(), TimeSpan.Zero); // not valid before the CA
        }
        var notAfter = now.AddDays(Math.Clamp(options.Value.Mtls.CertificateValidityDays, 1, 3650));
        if (notAfter > ca.NotAfter)
        {
            notAfter = ca.NotAfter;
        }

        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F; // positiv
        using var certificate = request.Create(ca, notBefore, notAfter, serial);

        return new IssuedCertificate(
            certificate.ExportCertificatePem(),
            Convert.ToHexStringLower(certificate.GetCertHash(HashAlgorithmName.SHA256)),
            new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero));
    }

    /// <summary>
    /// Validates a presented client certificate: issued by the agent CA, within its validity period, client authentication,
    /// subject = agent ID and thumbprint as stored for the agent.
    /// </summary>
    public bool IsValid(X509Certificate2 certificate, Guid agentId, string expectedThumbprint, DateTimeOffset now)
    {
        if (!string.Equals(
                Convert.ToHexStringLower(certificate.GetCertHash(HashAlgorithmName.SHA256)),
                expectedThumbprint, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (certificate.GetNameInfo(X509NameType.SimpleName, false) != agentId.ToString("D"))
        {
            return false;
        }

        var usage = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        if (usage is null || !usage.EnhancedKeyUsages.Cast<Oid>().Any(o => o.Value == ClientAuthOid))
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(Ca);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationTime = now.UtcDateTime;

        return chain.Build(certificate) &&
               chain.ChainElements.Count == 2 &&
               chain.ChainElements[1].Certificate.Thumbprint == Ca.Thumbprint;
    }

    public void Dispose() => _ca?.Dispose();
}
