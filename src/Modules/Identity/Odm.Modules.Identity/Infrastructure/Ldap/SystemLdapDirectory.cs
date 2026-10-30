using System.DirectoryServices.Protocols;
using System.Net;
using Microsoft.Extensions.Options;

namespace Odm.Modules.Identity.Infrastructure.Ldap;

/// <summary>
/// Verzeichniszugriff über System.DirectoryServices.Protocols (offiziell, ohne Zusatzpakete von Dritten).
/// Unter Linux wird libldap gebraucht; die Prüfung des Serverzertifikats richtet sich nach dem System
/// (z. B. /etc/ldap/ldap.conf, TLS_CACERT) und wird hier nicht abgeschaltet.
/// </summary>
public sealed class SystemLdapDirectory(IOptions<LdapOptions> options) : ILdapDirectory
{
    private const int InvalidCredentials = 49;

    public Task<LdapUser?> FindUserAsync(string userName, CancellationToken ct) =>
        Task.Run(() => Find(userName), ct);

    public Task<bool> VerifyPasswordAsync(string userDn, string password, CancellationToken ct) =>
        Task.Run(() => Verify(userDn, password), ct);

    private LdapUser? Find(string userName)
    {
        var o = options.Value;
        using var connection = Open();

        if (string.IsNullOrEmpty(o.BindDn))
        {
            connection.AuthType = AuthType.Anonymous;
        }
        else
        {
            connection.AuthType = AuthType.Basic;
            connection.Credential = new NetworkCredential(o.BindDn, o.BindPassword);
        }

        connection.Bind();

        var filter = o.UserFilter.Replace("{0}", LdapFilter.Escape(userName.Trim()), StringComparison.Ordinal);
        var request = new SearchRequest(o.BaseDn, filter, SearchScope.Subtree, o.DisplayNameAttribute, o.GroupAttribute)
        {
            SizeLimit = 2,
            TimeLimit = TimeSpan.FromSeconds(o.TimeoutSeconds)
        };

        var response = (SearchResponse)connection.SendRequest(request);
        if (response.Entries.Count != 1)
        {
            return null; // kein Treffer oder mehrdeutig
        }

        var entry = response.Entries[0];
        return new LdapUser(
            entry.DistinguishedName,
            Values(entry, o.DisplayNameAttribute).FirstOrDefault(),
            Values(entry, o.GroupAttribute));
    }

    private bool Verify(string userDn, string password)
    {
        using var connection = Open();
        connection.AuthType = AuthType.Basic;
        connection.Credential = new NetworkCredential(userDn, password);

        try
        {
            connection.Bind();
            return true;
        }
        catch (LdapException ex) when (ex.ErrorCode == InvalidCredentials)
        {
            return false;
        }
    }

    private LdapConnection Open()
    {
        var o = options.Value;
        var connection = new LdapConnection(new LdapDirectoryIdentifier(o.Host, o.Port))
        {
            Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds)
        };

        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;

        if (o.Security == LdapSecurity.Ldaps)
        {
            connection.SessionOptions.SecureSocketLayer = true;
        }
        else
        {
            connection.SessionOptions.StartTransportLayerSecurity(null);
        }

        return connection;
    }

    private static List<string> Values(SearchResultEntry entry, string attribute)
    {
        if (!entry.Attributes.Contains(attribute))
        {
            return [];
        }

        return entry.Attributes[attribute].GetValues(typeof(string)).Cast<string>().ToList();
    }
}
