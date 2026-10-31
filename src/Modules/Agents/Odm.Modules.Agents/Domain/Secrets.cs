using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Odm.Modules.Agents.Domain;

/// <summary>
/// Random, high-entropy secrets (registration token, agent secret).
/// Because of the high entropy a fast hash (SHA-256) is sufficient; PBKDF2 is only needed for passwords.
/// </summary>
public static class Secrets
{
    public static string NewSecret(string prefix = "") =>
        prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));
}
