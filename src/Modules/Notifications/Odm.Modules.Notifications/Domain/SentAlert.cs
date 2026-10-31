namespace Odm.Modules.Notifications.Domain;

/// <summary>An alert that has been sent. The unique key is what prevents sending the same alert again.</summary>
public sealed class SentAlert
{
    public Guid Id { get; private set; }
    public string Key { get; private set; } = "";
    public string Type { get; private set; } = "";
    public Guid? DeviceId { get; private set; }
    public string? DeviceName { get; private set; }
    public string Subject { get; private set; } = "";
    public string Detail { get; private set; } = "";
    public DateTimeOffset SentAt { get; private set; }

    /// <summary>Channels that accepted the digest, e.g. <c>email,webhook</c>.</summary>
    public string Channels { get; private set; } = "";

    private SentAlert() { }

    public static SentAlert Create(
        string key, string type, Guid? deviceId, string? deviceName, string subject, string detail,
        DateTimeOffset sentAt, string channels) => new()
    {
        Id = Guid.NewGuid(),
        Key = Cut(key, 256)!,
        Type = Cut(type, 64)!,
        DeviceId = deviceId,
        DeviceName = Cut(deviceName, 256),
        Subject = Cut(subject, 256) ?? "",
        Detail = Cut(detail, 1024) ?? "",
        SentAt = sentAt,
        Channels = Cut(channels, 64) ?? ""
    };

    private static string? Cut(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
