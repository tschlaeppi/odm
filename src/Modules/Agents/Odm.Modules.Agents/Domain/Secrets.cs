using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Odm.Modules.Agents.Domain;

/// <summary>
/// Zufällige, hochentropische Geheimnisse (Registrierungs-Token, Agent-Secret).
/// Wegen der hohen Entropie genügt ein schneller Hash (SHA-256); PBKDF2 ist nur für Passwörter nötig.
/// </summary>
public static class Secrets
{
    public static string NewSecret(string prefix = "") =>
        prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));
}
