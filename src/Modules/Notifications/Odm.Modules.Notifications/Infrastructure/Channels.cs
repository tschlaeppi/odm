using System.Net.Http.Json;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using Odm.Modules.Notifications.Application;

namespace Odm.Modules.Notifications.Infrastructure;

public interface IAlertChannel
{
    string Name { get; }
    bool Configured { get; }
    Task SendAsync(IReadOnlyCollection<AlertLine> alerts, CancellationToken ct);
}

/// <summary>E-mail through SMTP (STARTTLS or unencrypted relay; implicit TLS on port 465 is not supported).</summary>
public sealed class EmailChannel(IOptions<NotificationOptions> options) : IAlertChannel
{
    public string Name => "email";
    public bool Configured => options.Value.Email.Configured;

    public async Task SendAsync(IReadOnlyCollection<AlertLine> alerts, CancellationToken ct)
    {
        var settings = options.Value.Email;
        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.Security == SmtpSecurity.StartTls,
            Timeout = 30_000
        };
        if (settings.UserName.Length > 0)
        {
            client.Credentials = new System.Net.NetworkCredential(settings.UserName, settings.Password);
        }

        using var message = new MailMessage { From = new MailAddress(settings.From) };
        foreach (var to in settings.To.Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            message.To.Add(to.Trim());
        }

        message.Subject = AlertFormatter.Subject(alerts);
        message.Body = AlertFormatter.Body(alerts);
        await client.SendMailAsync(message, ct);
    }
}

/// <summary>JSON POST to a webhook URL.</summary>
public sealed class WebhookChannel(IOptions<NotificationOptions> options, IHttpClientFactory clients) : IAlertChannel
{
    public const string HttpClientName = "odm-webhook";

    public string Name => "webhook";
    public bool Configured => options.Value.Webhook.Configured;

    public async Task SendAsync(IReadOnlyCollection<AlertLine> alerts, CancellationToken ct)
    {
        var settings = options.Value.Webhook;
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Url)
        {
            Content = JsonContent.Create(new
            {
                text = AlertFormatter.Subject(alerts) + "\n" + AlertFormatter.Body(alerts),
                alerts = alerts.Select(a => new { a.Type, a.Key, device = a.DeviceName, a.Subject, a.Detail })
            })
        };
        if (settings.Authorization.Length > 0)
        {
            request.Headers.TryAddWithoutValidation("Authorization", settings.Authorization);
        }

        using var response = await clients.CreateClient(HttpClientName).SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
