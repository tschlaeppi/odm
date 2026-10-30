using Odm.Modules.Identity.Domain;

namespace Odm.Modules.Identity.Infrastructure.Ldap;

public enum LdapSecurity
{
    /// <summary>Verschlüsselt von Anfang an (LDAPS, meist Port 636).</summary>
    Ldaps = 0,

    /// <summary>Normale Verbindung (meist Port 389), danach sofort auf TLS umgestellt.</summary>
    StartTls = 1
}

/// <summary>
/// Anmeldung gegen ein Verzeichnis (Active Directory, OpenLDAP u. a.). Passwörter gehen nie unverschlüsselt über
/// das Netz: es gibt bewusst keine Einstellung für eine Verbindung ohne TLS.
/// </summary>
public sealed class LdapOptions
{
    public const string SectionName = "Identity:Ldap";

    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 636;
    public LdapSecurity Security { get; set; } = LdapSecurity.Ldaps;
    public string BaseDn { get; set; } = "";

    /// <summary>Dienstkonto zum Suchen des Benutzers; leer = anonyme Suche (nur wenn das Verzeichnis sie erlaubt).</summary>
    public string BindDn { get; set; } = "";

    public string BindPassword { get; set; } = "";

    /// <summary>Suchfilter, <c>{0}</c> steht für den eingegebenen (maskierten) Benutzernamen. AD: <c>(sAMAccountName={0})</c>, OpenLDAP: <c>(uid={0})</c>.</summary>
    public string UserFilter { get; set; } = "(sAMAccountName={0})";

    public string DisplayNameAttribute { get; set; } = "displayName";

    /// <summary>Attribut mit den Gruppen des Benutzers (DNs). AD und OpenLDAP mit memberOf-Overlay: <c>memberOf</c>.</summary>
    public string GroupAttribute { get; set; } = "memberOf";

    /// <summary>Gruppen-DNs je Rolle. Ohne passende Gruppe wird die Anmeldung abgelehnt.</summary>
    public string AdminGroup { get; set; } = "";

    public string OperatorGroup { get; set; } = "";
    public string ReadOnlyGroup { get; set; } = "";

    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Beschreibt das erste Problem der Konfiguration oder liefert null.</summary>
    public string? Problem()
    {
        if (string.IsNullOrWhiteSpace(Host)) return "Host fehlt.";
        if (Port is < 1 or > 65535) return "Port ist ungültig.";
        if (string.IsNullOrWhiteSpace(BaseDn)) return "BaseDn fehlt.";
        if (!UserFilter.Contains("{0}", StringComparison.Ordinal)) return "UserFilter muss {0} enthalten.";
        if (TimeoutSeconds is < 1 or > 120) return "TimeoutSeconds muss zwischen 1 und 120 liegen.";
        if (!string.IsNullOrEmpty(BindDn) && string.IsNullOrEmpty(BindPassword)) return "BindPassword fehlt.";
        if (string.IsNullOrWhiteSpace(AdminGroup) && string.IsNullOrWhiteSpace(OperatorGroup) &&
            string.IsNullOrWhiteSpace(ReadOnlyGroup))
        {
            return "Mindestens eine Gruppe (AdminGroup, OperatorGroup oder ReadOnlyGroup) ist nötig.";
        }

        return null;
    }
}

/// <summary>Ein Benutzer aus dem Verzeichnis: DN, Anzeigename und Gruppen-DNs.</summary>
public sealed record LdapUser(string Dn, string? DisplayName, IReadOnlyList<string> Groups);

/// <summary>Zugriff auf das Verzeichnis; austauschbar, damit die Anmeldelogik ohne Server testbar ist.</summary>
public interface ILdapDirectory
{
    /// <summary>Sucht genau einen Benutzer; bei keinem oder mehreren Treffern null.</summary>
    Task<LdapUser?> FindUserAsync(string userName, CancellationToken ct);

    /// <summary>Prüft das Passwort mit einer Anmeldung als der Benutzer.</summary>
    Task<bool> VerifyPasswordAsync(string userDn, string password, CancellationToken ct);
}

public static class LdapRoleMapper
{
    /// <summary>Höchste Rolle, für die der Benutzer in einer konfigurierten Gruppe ist; null = keine.</summary>
    public static UserRole? Map(IEnumerable<string> userGroups, LdapOptions options)
    {
        var groups = userGroups.Select(NormalizeDn).ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool In(string configured) => !string.IsNullOrWhiteSpace(configured) && groups.Contains(NormalizeDn(configured));

        if (In(options.AdminGroup)) return UserRole.Admin;
        if (In(options.OperatorGroup)) return UserRole.Operator;
        if (In(options.ReadOnlyGroup)) return UserRole.ReadOnly;
        return null;
    }

    /// <summary>Vergleichsform eines DN: ohne Leerraum um Kommas und Gleichheitszeichen.</summary>
    public static string NormalizeDn(string dn) =>
        string.Join(',', dn.Split(',').Select(part =>
        {
            var eq = part.IndexOf('=');
            return eq < 0 ? part.Trim() : part[..eq].Trim() + "=" + part[(eq + 1)..].Trim();
        }));
}

public static class LdapFilter
{
    /// <summary>Maskiert Sonderzeichen nach RFC 4515, damit Eingaben nie den Filter verändern (LDAP-Injection).</summary>
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
