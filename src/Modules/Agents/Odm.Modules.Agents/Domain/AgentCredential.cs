using Odm.SharedKernel;

namespace Odm.Modules.Agents.Domain;

/// <summary>Zugangsdaten eines registrierten Agents (je Installation).</summary>
public sealed class AgentCredential : Entity
{
    public Guid DeviceId { get; private set; }
    public byte[] SecretHash { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastSeenAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

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
