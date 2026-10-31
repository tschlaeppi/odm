using System.Text;

namespace Odm.Modules.Notifications.Application;

/// <summary>Builds the text of a notification message. No secrets are ever part of an alert.</summary>
public static class AlertFormatter
{
    public static string Subject(IReadOnlyCollection<AlertLine> alerts) =>
        alerts.Count == 1 ? $"[ODM] {Title(alerts.First())}" : $"[ODM] {alerts.Count} alerts";

    public static string Body(IReadOnlyCollection<AlertLine> alerts)
    {
        var text = new StringBuilder();
        foreach (var alert in alerts)
        {
            text.Append("- ").AppendLine(Title(alert));
            if (alert.Detail.Length > 0)
            {
                text.Append("  ").AppendLine(alert.Detail.ReplaceLineEndings(" "));
            }
        }

        return text.ToString().TrimEnd();
    }

    private static string Title(AlertLine alert)
    {
        var what = alert.Type switch
        {
            "installation-failed" => "Software installation failed",
            "device-silent" => "Device without heartbeat",
            "update-run-failed" => "OS update run failed",
            "certificate-expiring" => "Agent certificate expiring",
            _ => alert.Type
        };

        return alert.DeviceName is { Length: > 0 } device && !alert.Subject.Equals(device, StringComparison.OrdinalIgnoreCase)
            ? $"{what}: {alert.Subject} on {device}"
            : $"{what}: {alert.Subject}";
    }
}
