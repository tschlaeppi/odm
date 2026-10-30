using Odm.SharedKernel;

namespace Odm.Modules.Software.Domain;

/// <summary>Benannter Wert (z. B. Lizenzschlüssel), verschlüsselt gespeichert. Argumente verweisen mit {{secret:NAME}} darauf.</summary>
public sealed class SoftwareSecret : Entity
{
    public string Name { get; private set; } = "";
    public byte[] Ciphertext { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private SoftwareSecret() { }

    public static SoftwareSecret Create(string name, byte[] ciphertext)
    {
        var now = DateTimeOffset.UtcNow;
        return new SoftwareSecret { Name = name, Ciphertext = ciphertext, CreatedAt = now, UpdatedAt = now };
    }

    public void Replace(byte[] ciphertext)
    {
        Ciphertext = ciphertext;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
