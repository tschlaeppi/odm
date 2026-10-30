using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Odm.Modules.Agents.Application;
using Xunit;

namespace Odm.Tests;

public class AgentCertificateAuthorityTests : IDisposable
{
    private readonly List<string> _dirs = [];

    public void Dispose()
    {
        foreach (var dir in _dirs)
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private AgentCertificateAuthority NewAuthority(string? directory = null, int validityDays = 365)
    {
        var dir = directory ?? Directory.CreateTempSubdirectory("odm-pki").FullName;
        if (directory is null)
        {
            _dirs.Add(dir);
        }

        var options = new AgentsOptions
        {
            Mtls = new AgentMtlsOptions { Mode = AgentMtlsMode.Optional, CaDirectory = dir, CertificateValidityDays = validityDays }
        };
        return new AgentCertificateAuthority(Options.Create(options), new Env());
    }

    private static (ECDsa Key, string Csr) NewRequest(string subject = "CN=odm-agent")
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key, new CertificateRequest(subject, key, HashAlgorithmName.SHA256).CreateSigningRequestPem());
    }

    private static X509Certificate2 WithKey(IssuedCertificate issued, ECDsa key) =>
        X509Certificate2.CreateFromPem(issued.CertificatePem, key.ExportPkcs8PrivateKeyPem());

    [Fact]
    public void Issued_certificate_is_valid_for_its_agent()
    {
        var ca = NewAuthority();
        var agent = Guid.NewGuid();
        var (key, csr) = NewRequest();

        var issued = ca.Issue(csr, agent, DateTimeOffset.UtcNow);
        using var cert = WithKey(issued, key);

        Assert.Equal(agent.ToString("D"), cert.GetNameInfo(X509NameType.SimpleName, false));
        Assert.True(ca.IsValid(cert, agent, issued.Thumbprint, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Subject_from_the_request_is_ignored()
    {
        var ca = NewAuthority();
        var agent = Guid.NewGuid();
        var (key, csr) = NewRequest("CN=someone-else, O=evil");

        var issued = ca.Issue(csr, agent, DateTimeOffset.UtcNow);
        using var cert = WithKey(issued, key);

        Assert.Equal($"CN={agent:D}", cert.Subject);
    }

    [Fact]
    public void Certificate_is_bound_to_agent_id_and_thumbprint()
    {
        var ca = NewAuthority();
        var agent = Guid.NewGuid();
        var (key, csr) = NewRequest();
        var issued = ca.Issue(csr, agent, DateTimeOffset.UtcNow);
        using var cert = WithKey(issued, key);

        Assert.False(ca.IsValid(cert, Guid.NewGuid(), issued.Thumbprint, DateTimeOffset.UtcNow));
        Assert.False(ca.IsValid(cert, agent, new string('0', 64), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Certificates_from_another_ca_are_rejected()
    {
        var ours = NewAuthority();
        var foreign = NewAuthority();
        var agent = Guid.NewGuid();
        var (key, csr) = NewRequest();
        var issuedByForeign = foreign.Issue(csr, agent, DateTimeOffset.UtcNow);
        using var cert = WithKey(issuedByForeign, key);

        Assert.False(ours.IsValid(cert, agent, issuedByForeign.Thumbprint, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Expired_certificates_are_rejected()
    {
        var ca = NewAuthority(validityDays: 30);
        var agent = Guid.NewGuid();
        var (key, csr) = NewRequest();
        var issued = ca.Issue(csr, agent, DateTimeOffset.UtcNow);
        using var cert = WithKey(issued, key);

        Assert.True(ca.IsValid(cert, agent, issued.Thumbprint, DateTimeOffset.UtcNow));
        Assert.False(ca.IsValid(cert, agent, issued.Thumbprint, DateTimeOffset.UtcNow.AddDays(100)));
    }

    [Fact]
    public void Only_ecdsa_p256_requests_are_accepted()
    {
        var ca = NewAuthority();
        using var rsa = RSA.Create(2048);
        var rsaCsr = new CertificateRequest("CN=x", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).CreateSigningRequestPem();
        Assert.Throws<CryptographicException>(() => ca.Issue(rsaCsr, Guid.NewGuid(), DateTimeOffset.UtcNow));

        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var p384Csr = new CertificateRequest("CN=x", p384, HashAlgorithmName.SHA256).CreateSigningRequestPem();
        Assert.Throws<CryptographicException>(() => ca.Issue(p384Csr, Guid.NewGuid(), DateTimeOffset.UtcNow));

        Assert.ThrowsAny<CryptographicException>(() => ca.Issue("not a csr", Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Ca_is_persisted_and_reloaded()
    {
        var dir = Directory.CreateTempSubdirectory("odm-pki").FullName;
        _dirs.Add(dir);

        var first = NewAuthority(dir);
        var pem = first.CaCertificatePem;
        var agent = Guid.NewGuid();
        var (key, csr) = NewRequest();
        var issued = first.Issue(csr, agent, DateTimeOffset.UtcNow);

        var second = NewAuthority(dir);
        Assert.Equal(pem, second.CaCertificatePem);
        using var cert = WithKey(issued, key);
        Assert.True(second.IsValid(cert, agent, issued.Thumbprint, DateTimeOffset.UtcNow));

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(dir, "ca.key")));
        }
    }

    private sealed class Env : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
