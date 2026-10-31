using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Odm.Modules.Software.Application;

namespace Odm.Modules.Software.Infrastructure;

/// <summary>
/// Encrypts secrets with AES-256-GCM. The key comes from the configuration (Secrets:MasterKey) and
/// is not stored in the database. The name is included as additional authenticated data
/// so that a value cannot be reused under a different name.
/// Format: Nonce (12 Byte) | Tag (16 Byte) | Chiffretext.
/// </summary>
public sealed class SecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public SecretProtector(IOptions<SecretsOptions> options)
    {
        _key = Convert.FromBase64String(options.Value.MasterKey);
    }

    public byte[] Protect(string name, string plaintext)
    {
        var data = Encoding.UTF8.GetBytes(plaintext);
        var result = new byte[NonceSize + TagSize + data.Length];
        var nonce = result.AsSpan(0, NonceSize);
        var tag = result.AsSpan(NonceSize, TagSize);
        var cipher = result.AsSpan(NonceSize + TagSize);

        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, data, cipher, tag, Encoding.UTF8.GetBytes(name));
        return result;
    }

    public string Unprotect(string name, byte[] protectedData)
    {
        var nonce = protectedData.AsSpan(0, NonceSize);
        var tag = protectedData.AsSpan(NonceSize, TagSize);
        var cipher = protectedData.AsSpan(NonceSize + TagSize);
        var data = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, data, Encoding.UTF8.GetBytes(name));
        return Encoding.UTF8.GetString(data);
    }
}
