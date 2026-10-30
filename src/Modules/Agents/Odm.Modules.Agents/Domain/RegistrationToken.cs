using Odm.SharedKernel;

namespace Odm.Modules.Agents.Domain;

/// <summary>Einmal verwendbares Token, mit dem sich ein neuer Agent registrieren darf.</summary>
public sealed class RegistrationToken : Entity
{
    public const string Prefix = "odm-reg-";

    public string Label { get; private set; } = "";
    public byte[] TokenHash { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }

    private RegistrationToken() { }

    /// <summary>Erzeugt ein Token. Der Klartext wird nur hier zurückgegeben und nie gespeichert.</summary>
    public static (RegistrationToken Token, string Plain) Issue(string label, TimeSpan validity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        var plain = Secrets.NewSecret(Prefix);
        var now = DateTimeOffset.UtcNow;
        var token = new RegistrationToken
        {
            Label = label.Trim(),
            TokenHash = Secrets.Hash(plain),
            CreatedAt = now,
            ExpiresAt = now + validity
        };
        return (token, plain);
    }

    public string StatusAt(DateTimeOffset now) =>
        UsedAt is not null ? "Used" : ExpiresAt <= now ? "Expired" : "Active";
}
