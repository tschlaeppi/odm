using System.Security.Cryptography;
using System.Text;

namespace Odm.Modules.Identity.Infrastructure.Ldap;

/// <summary>Stable user ID for accounts from external directories (they have no row in the local database).</summary>
public static class ExternalUserIds
{
    public static Guid Create(string provider, string normalizedUserName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{provider}\n{normalizedUserName}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
