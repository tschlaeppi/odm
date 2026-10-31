using Odm.SharedKernel;

namespace Odm.Modules.Agents.Domain;

/// <summary>Credentials of a registered agent (per installation).</summary>
public sealed class AgentCredential : Entity
{
    public Guid DeviceId { get; private set; }
    public byte[] SecretHash { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastSeenAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>SHA-256 thumbprint (hex) of the issued client certificate; null = none issued yet.</summary>
    public string? CertThumbprint { get; private set; }

    public DateTimeOffset? CertNotAfter { get; private set; }

    public void SetCertificate(string thumbprint, DateTimeOffset notAfter)
    {
        CertThumbprint = thumbprint;
        CertNotAfter = notAfter;
    }

    private AgentCredential() { }

    public static (AgentCredential Credential, string Secret) Issue(Guid deviceId)
    {
        var secret = Secrets.NewSecret();
        var credential = new AgentCredential
        {
            DeviceId = deviceId,
            SecretHash = Secrets.Hash(secret),
            CreatedAt = DateTimeOffset.UtcNow
        };
        return (credential, secret);
    }
}
