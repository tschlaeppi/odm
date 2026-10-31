namespace Odm.Modules.Notifications.Application;

public enum SmtpSecurity
{
    /// <summary>No encryption (only for a local relay).</summary>
    None,

    /// <summary>Connect unencrypted, then upgrade with STARTTLS (usually port 587).</summary>
    StartTls
}

public sealed class EmailOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
    public string[] To { get; set; } = [];

    public bool Configured => Host.Length > 0 && From.Length > 0 && To.Length > 0;
}

public sealed class WebhookOptions
{
    /// <summary>Receives a JSON POST: <c>{ "text": "...", "alerts": [ ... ] }</c> (works with Slack, Mattermost, Teams workflows).</summary>
    public string Url { get; set; } = "";

    /// <summary>Optional value for the <c>Authorization</c> header, e.g. <c>Bearer abc</c>.</summary>
    public string Authorization { get; set; } = "";

    public bool Configured => Uri.TryCreate(Url, UriKind.Absolute, out var uri) &&
                              (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>Master switch; off by default.</summary>
    public bool Enabled { get; set; }

    /// <summary>How often alert sources are checked.</summary>
    public int CheckMinutes { get; set; } = 5;

    /// <summary>Alert types to send (<see cref="Odm.SharedKernel.Contracts.AlertTypes"/>); empty = all.</summary>
    public string[] Events { get; set; } = [];

    /// <summary>At most this many alerts go into one message; the rest follows in the next round.</summary>
    public int MaxPerMessage { get; set; } = 50;

    /// <summary>Sent-alert records older than this many days are deleted (0 = keep).</summary>
    public int RetentionDays { get; set; } = 180;

    public EmailOptions Email { get; set; } = new();
    public WebhookOptions Webhook { get; set; } = new();

    public bool EventEnabled(string type) =>
        Events.Length == 0 || Events.Contains(type, StringComparer.OrdinalIgnoreCase);
}

/// <summary>One alert in a message, with the device name resolved.</summary>
public sealed record AlertLine(string Type, string Key, string? DeviceName, string Subject, string Detail);

public sealed record SentAlertDto(
    Guid Id, string Type, string? DeviceName, string Subject, string Detail, DateTimeOffset SentAt, string Channels);

public sealed record ChannelInfo(string Name, bool Configured);

public sealed record ChannelResult(string Channel, bool Success, string? Error);
