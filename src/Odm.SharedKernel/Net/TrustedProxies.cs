using System.Net;
using Microsoft.Extensions.Configuration;

namespace Odm.SharedKernel.Net;

/// <summary>
/// Addresses or CIDR ranges of reverse proxies that ODM trusts (configuration: <c>Proxy:TrustedProxies</c>).
/// Only these peers may set forwarded headers or hand on a client certificate.
/// </summary>
public sealed class TrustedProxies
{
    public const string SectionName = "Proxy:TrustedProxies";

    private readonly List<IPNetwork> _networks;

    public TrustedProxies(IEnumerable<string> entries)
    {
        _networks = [];
        foreach (var entry in entries)
        {
            var text = entry.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (!text.Contains('/'))
            {
                text += IPAddress.TryParse(text, out var single) && single.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                    ? "/128"
                    : "/32";
            }

            if (!IPNetwork.TryParse(text, out var network))
            {
                throw new InvalidOperationException($"Invalid entry in {SectionName}: '{entry}'. Use an IP address or CIDR range.");
            }

            _networks.Add(network);
        }
    }

    public static TrustedProxies FromConfiguration(IConfiguration configuration) =>
        new(configuration.GetSection(SectionName).Get<string[]>() ?? []);

    public IReadOnlyList<IPNetwork> Networks => _networks;

    public bool Any => _networks.Count > 0;

    public bool Contains(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return _networks.Any(n => n.Contains(address));
    }
}
