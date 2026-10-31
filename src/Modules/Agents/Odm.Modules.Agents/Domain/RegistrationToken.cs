using Odm.SharedKernel;

namespace Odm.Modules.Agents.Domain;

/// <summary>
/// Token with which a new agent may register. Single-use by default; a higher <see cref="MaxUses"/> makes it reusable
/// (e.g. for a VM image or provisioning script) until it has been used that often or expires.
/// </summary>
public sealed class RegistrationToken : Entity
{
    public const string Prefix = "odm-reg-";

    public string Label { get; private set; } = "";
    public byte[] TokenHash { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    /// <summary>Time of the last registration with this token.</summary>
    public DateTimeOffset? UsedAt { get; private set; }

    public const int MaxUsesLimit = 1000;

    /// <summary>How many agents may register with the token (1 = single-use).</summary>
    public int MaxUses { get; private set; } = 1;

    public int UseCount { get; private set; }

    private RegistrationToken() { }

    /// <summary>Creates a token. The plaintext is returned only here and never stored.</summary>
    public static (RegistrationToken Token, string Plain) Issue(string label, TimeSpan validity, int maxUses = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        var plain = Secrets.NewSecret(Prefix);
        var now = DateTimeOffset.UtcNow;
        var token = new RegistrationToken
        {
            Label = label.Trim(),
            TokenHash = Secrets.Hash(plain),
            CreatedAt = now,
            ExpiresAt = now + validity,
            MaxUses = Math.Clamp(maxUses, 1, MaxUsesLimit)
        };
        return (token, plain);
    }

    /// <summary>
    /// All uses consumed. Single-use tokens that were used before reusable tokens existed have UseCount 0 but a UsedAt value,
    /// so they count as used up as well.
    /// </summary>
    public bool IsUsedUp => UseCount >= MaxUses || (MaxUses == 1 && UsedAt is not null);

    public string StatusAt(DateTimeOffset now) =>
        IsUsedUp ? "Used" : ExpiresAt <= now ? "Expired" : "Active";
}
