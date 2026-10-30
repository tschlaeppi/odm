using System.Security.Cryptography;
using System.Text;

namespace Odm.Modules.Identity.Infrastructure.Ldap;

/// <summary>Stabile Benutzer-ID für Konten aus externen Verzeichnissen (sie haben keine Zeile in der lokalen Datenbank).</summary>
public static class ExternalUserIds
{
    public static Guid Create(string provider, string normalizedUserName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{provider}\n{normalizedUserName}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
