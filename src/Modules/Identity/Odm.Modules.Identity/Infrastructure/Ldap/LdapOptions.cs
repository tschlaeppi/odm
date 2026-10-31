using Odm.Modules.Identity.Domain;

namespace Odm.Modules.Identity.Infrastructure.Ldap;

public enum LdapSecurity
{
    /// <summary>Encrypted from the start (LDAPS, usually port 636).</summary>
    Ldaps = 0,

    /// <summary>Normale Verbindung (meist Port 389), danach sofort auf TLS umgestellt.</summary>
    StartTls = 1
}

/// <summary>
/// Sign-in against a directory (Active Directory, OpenLDAP and others). Passwords never travel unencrypted over
/// the network: there is deliberately no setting for a connection without TLS.
/// </summary>
public sealed class LdapOptions
{
    public const string SectionName = "Identity:Ldap";

    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 636;
    public LdapSecurity Security { get; set; } = LdapSecurity.Ldaps;
    public string BaseDn { get; set; } = "";

    /// <summary>Service account for searching the user; empty = anonymous search (only if the directory allows it).</summary>
    public string BindDn { get; set; } = "";

    public string BindPassword { get; set; } = "";

    /// <summary>Search filter, <c>{0}</c> stands for the entered (escaped) user name. AD: <c>(sAMAccountName={0})</c>, OpenLDAP: <c>(uid={0})</c>.</summary>
    public string UserFilter { get; set; } = "(sAMAccountName={0})";

    public string DisplayNameAttribute { get; set; } = "displayName";

    /// <summary>Attribute with the user's groups (DNs). AD and OpenLDAP with the memberOf overlay: <c>memberOf</c>.</summary>
    public string GroupAttribute { get; set; } = "memberOf";

    /// <summary>Group DNs per role. Without a matching group the sign-in is rejected.</summary>
    public string AdminGroup { get; set; } = "";

    public string OperatorGroup { get; set; } = "";
    public string ReadOnlyGroup { get; set; } = "";

    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Describes the first problem of the configuration or returns null.</summary>
    public string? Problem()
    {
        if (string.IsNullOrWhiteSpace(Host)) return "Host fehlt.";
        if (Port is < 1 or > 65535) return "Port is invalid.";
        if (string.IsNullOrWhiteSpace(BaseDn)) return "BaseDn fehlt.";
        if (!UserFilter.Contains("{0}", StringComparison.Ordinal)) return "UserFilter must contain {0}.";
        if (TimeoutSeconds is < 1 or > 120) return "TimeoutSeconds must be between 1 and 120.";
        if (!string.IsNullOrEmpty(BindDn) && string.IsNullOrEmpty(BindPassword)) return "BindPassword fehlt.";
        if (string.IsNullOrWhiteSpace(AdminGroup) && string.IsNullOrWhiteSpace(OperatorGroup) &&
            string.IsNullOrWhiteSpace(ReadOnlyGroup))
        {
            return "At least one group (AdminGroup, OperatorGroup or ReadOnlyGroup) is required.";
        }

        return null;
    }
}

/// <summary>A user from the directory: DN, display name and group DNs.</summary>
public sealed record LdapUser(string Dn, string? DisplayName, IReadOnlyList<string> Groups);

/// <summary>Access to the directory; replaceable so that the sign-in logic can be tested without a server.</summary>
public interface ILdapDirectory
{
    /// <summary>Finds exactly one user; null for no match or several matches.</summary>
    Task<LdapUser?> FindUserAsync(string userName, CancellationToken ct);

    /// <summary>Verifies the password by signing in as the user.</summary>
    Task<bool> VerifyPasswordAsync(string userDn, string password, CancellationToken ct);
}

public static class LdapRoleMapper
{
    /// <summary>Highest role for which the user is in a configured group; null = none.</summary>
    public static UserRole? Map(IEnumerable<string> userGroups, LdapOptions options)
    {
        var groups = userGroups.Select(NormalizeDn).ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool In(string configured) => !string.IsNullOrWhiteSpace(configured) && groups.Contains(NormalizeDn(configured));

        if (In(options.AdminGroup)) return UserRole.Admin;
        if (In(options.OperatorGroup)) return UserRole.Operator;
        if (In(options.ReadOnlyGroup)) return UserRole.ReadOnly;
        return null;
    }

    /// <summary>Comparison form of a DN: without whitespace around commas and equals signs.</summary>
    public static string NormalizeDn(string dn) =>
        string.Join(',', dn.Split(',').Select(part =>
        {
            var eq = part.IndexOf('=');
            return eq < 0 ? part.Trim() : part[..eq].Trim() + "=" + part[(eq + 1)..].Trim();
        }));
}

public static class LdapFilter
{
    /// <summary>Escapes special characters per RFC 4515 so that input never alters the filter (LDAP injection).</summary>
    public static string Escape(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\5c"); break;
                case '*': sb.Append("\\2a"); break;
                case '(': sb.Append("\\28"); break;
                case ')': sb.Append("\\29"); break;
                case '\0': sb.Append("\\00"); break;
                default: sb.Append(ch); break;
            }
        }

        return sb.ToString();
    }
}
