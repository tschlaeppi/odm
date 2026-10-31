using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Odm.Modules.Agents.Application;
using Odm.SharedKernel.Net;

namespace Odm.Modules.Agents.Infrastructure;

/// <summary>
/// Supplies the client certificate of a request: from the TLS connection when ODM terminates TLS itself, or from a header
/// when a trusted reverse proxy terminated TLS and forwarded the certificate (<c>Agents:Mtls:ForwardedCertificateHeader</c>).
/// The header is only honoured when the connection really comes from an address in <c>Proxy:TrustedProxies</c>;
/// the proxy must overwrite the header on every request.
/// </summary>
public sealed class ClientCertificateProvider(IOptions<AgentsOptions> options, TrustedProxies trusted)
{
    /// <summary><c>HttpContext.Items</c> key holding the address of the directly connected peer (set before forwarded headers are applied).</summary>
    public const string PeerItemKey = "odm.peer-address";

    private const int MaxHeaderLength = 16 * 1024;

    private string Header => options.Value.Mtls.ForwardedCertificateHeader?.Trim() ?? "";

    /// <summary>The request arrived through a trusted proxy that is configured to forward client certificates.</summary>
    public bool FromTrustedProxy(HttpContext http) =>
        Header.Length > 0 &&
        trusted.Any &&
        http.Items.TryGetValue(PeerItemKey, out var peer) &&
        peer is IPAddress address &&
        trusted.Contains(address);

    /// <summary>Can a client certificate reach ODM on this request (direct TLS or forwarded by a trusted proxy)?</summary>
    public bool CanPresentCertificate(HttpContext http) =>
        http.Features.Get<ITlsConnectionFeature>() is not null || FromTrustedProxy(http);

    public async Task<X509Certificate2?> GetAsync(HttpContext http, CancellationToken ct)
    {
        if (FromTrustedProxy(http))
        {
            // Never fall back to the connection's certificate: the proxy decides what is presented.
            return Parse(http.Request.Headers[Header].ToString());
        }

        return await http.Connection.GetClientCertificateAsync(ct);
    }

    /// <summary>
    /// Parses a forwarded certificate: URL-encoded PEM (nginx <c>$ssl_client_escaped_cert</c>), plain PEM or
    /// Base64 DER (Caddy, Traefik). Returns null for anything else.
    /// </summary>
    public static X509Certificate2? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxHeaderLength)
        {
            return null;
        }

        try
        {
            var text = value.Trim();
            if (text.Contains('%'))
            {
                text = Uri.UnescapeDataString(text);
            }

            if (text.Contains("-----BEGIN", StringComparison.Ordinal))
            {
                return X509Certificate2.CreateFromPem(text);
            }

            // Base64 DER; tolerate the URL-safe alphabet and missing padding.
            text = text.Replace('-', '+').Replace('_', '/').Replace(" ", "+");
            text = text.PadRight(text.Length + (4 - text.Length % 4) % 4, '=');
            return X509CertificateLoader.LoadCertificate(Convert.FromBase64String(text));
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException or ArgumentException or UriFormatException)
        {
            return null;
        }
    }
}
