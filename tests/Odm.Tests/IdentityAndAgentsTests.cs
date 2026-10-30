using Odm.Modules.Agents.Domain;
using Odm.Modules.Identity.Infrastructure;

namespace Odm.Tests;

public class PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Correct_password_verifies_and_wrong_one_does_not()
    {
        var hash = _hasher.Hash("Pa$$w0rt");

        Assert.True(_hasher.Verify("Pa$$w0rt", hash));
        Assert.False(_hasher.Verify("pa$$w0rt", hash));
        Assert.False(_hasher.Verify("", hash));
    }

    [Fact]
    public void Same_password_gives_different_hashes_and_the_hash_has_no_plaintext()
    {
        var a = _hasher.Hash("Pa$$w0rt");
        var b = _hasher.Hash("Pa$$w0rt");

        Assert.NotEqual(a, b); // zufälliges Salt
        Assert.DoesNotContain("Pa$$w0rt", a);
        Assert.StartsWith("v1.", a);
    }

    [Theory]
    [InlineData("")]
    [InlineData("kein-hash")]
    [InlineData("v1.abc.def.ghi")]
    [InlineData("v1.0.AAAA.AAAA")]
    [InlineData("v1.1000.@@@.@@@")]
    [InlineData("v2.1000.AAAA.AAAA")]
    public void Malformed_hashes_never_verify_and_never_throw(string hash) =>
        Assert.False(_hasher.Verify("egal", hash));
}

public class AgentSecretsTests
{
    [Fact]
    public void Secrets_are_random_and_prefixed()
    {
        var a = Secrets.NewSecret("odm-reg-");
        var b = Secrets.NewSecret("odm-reg-");

        Assert.StartsWith("odm-reg-", a);
        Assert.NotEqual(a, b);
        Assert.Equal("odm-reg-".Length + 43, a.Length); // 32 Byte als Base64Url ohne Auffüllung
    }

    [Fact]
    public void Hash_is_deterministic_and_256_bit()
    {
        Assert.Equal(Secrets.Hash("x"), Secrets.Hash("x"));
        Assert.NotEqual(Secrets.Hash("x"), Secrets.Hash("y"));
        Assert.Equal(32, Secrets.Hash("x").Length);
    }

    [Fact]
    public void Registration_token_is_stored_only_as_hash_and_expires()
    {
        var (token, plain) = RegistrationToken.Issue("PC-0001", TimeSpan.FromHours(1));
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(Secrets.Hash(plain), token.TokenHash);
        Assert.Equal("Active", token.StatusAt(now));
        Assert.Equal("Expired", token.StatusAt(now.AddHours(2)));
    }

    [Fact]
    public void Agent_credential_secret_matches_its_hash()
    {
        var (credential, secret) = AgentCredential.Issue(Guid.NewGuid());

        Assert.Equal(Secrets.Hash(secret), credential.SecretHash);
        Assert.Null(credential.RevokedAt);
    }
}
