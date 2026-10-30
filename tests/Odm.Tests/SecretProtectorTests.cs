using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Odm.Modules.Software.Application;
using Odm.Modules.Software.Infrastructure;

namespace Odm.Tests;

public class SecretProtectorTests
{
    private static SecretProtector Protector(byte fill = 7) =>
        new(Options.Create(new SecretsOptions { MasterKey = Convert.ToBase64String(Enumerable.Repeat(fill, 32).ToArray()) }));

    [Fact]
    public void Roundtrip()
    {
        var protector = Protector();
        var data = protector.Protect("LIZENZ", "geheim äöü 123");

        Assert.Equal("geheim äöü 123", protector.Unprotect("LIZENZ", data));
    }

    [Fact]
    public void Ciphertext_does_not_contain_the_plaintext_and_differs_each_time()
    {
        var protector = Protector();
        var a = protector.Protect("LIZENZ", "geheim");
        var b = protector.Protect("LIZENZ", "geheim");

        Assert.NotEqual(a, b); // zufällige Nonce
        Assert.DoesNotContain("geheim", System.Text.Encoding.UTF8.GetString(a));
    }

    [Fact]
    public void Value_cannot_be_reused_under_another_name()
    {
        var protector = Protector();
        var data = protector.Protect("LIZENZ", "geheim");

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect("ANDERER_NAME", data));
    }

    [Fact]
    public void Tampering_is_detected()
    {
        var protector = Protector();
        var data = protector.Protect("LIZENZ", "geheim");
        data[^1] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect("LIZENZ", data));
    }

    [Fact]
    public void Wrong_key_cannot_decrypt()
    {
        var data = Protector(7).Protect("LIZENZ", "geheim");

        Assert.ThrowsAny<CryptographicException>(() => Protector(8).Unprotect("LIZENZ", data));
    }

    [Theory]
    [InlineData(32, true)]
    [InlineData(16, false)]
    [InlineData(33, false)]
    public void Master_key_must_be_256_bit(int bytes, bool valid) =>
        Assert.Equal(valid, SecretsOptions.IsValid(Convert.ToBase64String(new byte[bytes])));

    [Theory]
    [InlineData("")]
    [InlineData("das ist kein base64!")]
    public void Master_key_must_be_base64(string key) => Assert.False(SecretsOptions.IsValid(key));
}
