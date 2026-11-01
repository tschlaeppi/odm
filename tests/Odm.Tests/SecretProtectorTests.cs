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
        var data = protector.Protect("LICENCE", "secret äöü 123");

        Assert.Equal("secret äöü 123", protector.Unprotect("LICENCE", data));
    }

    [Fact]
    public void Ciphertext_does_not_contain_the_plaintext_and_differs_each_time()
    {
        var protector = Protector();
        var a = protector.Protect("LICENCE", "geheim");
        var b = protector.Protect("LICENCE", "geheim");

        Assert.NotEqual(a, b); // random nonce
        Assert.DoesNotContain("geheim", System.Text.Encoding.UTF8.GetString(a));
    }

    [Fact]
    public void Value_cannot_be_reused_under_another_name()
    {
        var protector = Protector();
        var data = protector.Protect("LICENCE", "geheim");

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect("ANDERER_NAME", data));
    }

    [Fact]
    public void Tampering_is_detected()
    {
        var protector = Protector();
        var data = protector.Protect("LICENCE", "geheim");
        data[^1] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect("LICENCE", data));
    }

    [Fact]
    public void Wrong_key_cannot_decrypt()
    {
        var data = Protector(7).Protect("LICENCE", "geheim");

        Assert.ThrowsAny<CryptographicException>(() => Protector(8).Unprotect("LICENCE", data));
    }

    [Theory]
    [InlineData(32, true)]
    [InlineData(16, false)]
    [InlineData(33, false)]
    public void Master_key_must_be_256_bit(int bytes, bool valid) =>
        Assert.Equal(valid, SecretsOptions.IsValid(Convert.ToBase64String(new byte[bytes])));

    [Theory]
    [InlineData("")]
    [InlineData("this is not base64!")]
    public void Master_key_must_be_base64(string key) => Assert.False(SecretsOptions.IsValid(key));
}
