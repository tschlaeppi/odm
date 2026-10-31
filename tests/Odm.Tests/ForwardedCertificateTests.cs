using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Odm.Modules.Agents.Application;
using Odm.Modules.Agents.Infrastructure;
using Odm.SharedKernel.Net;
using Xunit;

namespace Odm.Tests;

public class ForwardedCertificateTests
{
    private static X509Certificate2 NewCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=test", key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    [Fact]
    public void Parses_url_encoded_pem()
    {
        using var cert = NewCertificate();
        var header = Uri.EscapeDataString(cert.ExportCertificatePem());

        using var parsed = ClientCertificateProvider.Parse(header);

        Assert.NotNull(parsed);
        Assert.Equal(cert.Thumbprint, parsed!.Thumbprint);
    }

    [Fact]
    public void Parses_plain_pem()
    {
        using var cert = NewCertificate();

        using var parsed = ClientCertificateProvider.Parse(cert.ExportCertificatePem());

        Assert.Equal(cert.Thumbprint, parsed?.Thumbprint);
    }

    [Fact]
    public void Parses_base64_der_with_and_without_padding()
    {
        using var cert = NewCertificate();
        var base64 = Convert.ToBase64String(cert.RawData);

        using var padded = ClientCertificateProvider.Parse(base64);
        using var unpadded = ClientCertificateProvider.Parse(base64.TrimEnd('='));

        Assert.Equal(cert.Thumbprint, padded?.Thumbprint);
        Assert.Equal(cert.Thumbprint, unpadded?.Thumbprint);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a certificate")]
    [InlineData("-----BEGIN CERTIFICATE-----\nZm9v\n-----END CERTIFICATE-----")]
    [InlineData("AAAA")]
    public void Garbage_is_rejected(string? value) => Assert.Null(ClientCertificateProvider.Parse(value));

    [Fact]
    public void Oversized_header_is_rejected() =>
        Assert.Null(ClientCertificateProvider.Parse(new string('A', 20_000)));

    [Theory]
    [InlineData("10.0.0.5", true)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.31.250.7", true)]
    [InlineData("::ffff:172.31.250.7", true)]
    [InlineData("192.168.1.1", false)]
    public void Trusted_proxies_match_addresses_and_ranges(string address, bool expected)
    {
        var trusted = new TrustedProxies(["10.0.0.5", "172.31.250.0/24"]);

        Assert.Equal(expected, trusted.Contains(IPAddress.Parse(address)));
    }

    [Fact]
    public void Invalid_trusted_proxy_entry_is_reported() =>
        Assert.Throws<InvalidOperationException>(() => new TrustedProxies(["not-an-address"]));

    private static ClientCertificateProvider NewProvider(string header, params string[] trusted) =>
        new(Options.Create(new AgentsOptions { Mtls = new AgentMtlsOptions { ForwardedCertificateHeader = header } }),
            new TrustedProxies(trusted));

    private static DefaultHttpContext NewContext(string peer)
    {
        var context = new DefaultHttpContext();
        context.Items[ClientCertificateProvider.PeerItemKey] = IPAddress.Parse(peer);
        return context;
    }

    [Fact]
    public async Task Header_is_used_only_for_trusted_peers()
    {
        using var cert = NewCertificate();
        var provider = NewProvider("X-Client-Cert", "10.0.0.0/24");

        var trustedContext = NewContext("10.0.0.9");
        trustedContext.Request.Headers["X-Client-Cert"] = Convert.ToBase64String(cert.RawData);
        var untrustedContext = NewContext("203.0.113.9");
        untrustedContext.Request.Headers["X-Client-Cert"] = Convert.ToBase64String(cert.RawData);

        Assert.True(provider.FromTrustedProxy(trustedContext));
        Assert.Equal(cert.Thumbprint, (await provider.GetAsync(trustedContext, default))?.Thumbprint);

        Assert.False(provider.FromTrustedProxy(untrustedContext));
        Assert.False(provider.CanPresentCertificate(untrustedContext));
        Assert.Null(await provider.GetAsync(untrustedContext, default));
    }

    [Fact]
    public void Forwarding_is_off_without_header_name_or_trusted_proxies()
    {
        var context = NewContext("10.0.0.9");

        Assert.False(NewProvider("", "10.0.0.0/24").FromTrustedProxy(context));
        Assert.False(NewProvider("X-Client-Cert").FromTrustedProxy(context));
    }
}
