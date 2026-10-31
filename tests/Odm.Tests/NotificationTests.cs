using Odm.Modules.Notifications.Application;
using Odm.SharedKernel.Contracts;
using Xunit;

namespace Odm.Tests;

public class NotificationTests
{
    [Fact]
    public void Single_alert_has_its_title_as_subject()
    {
        var alerts = new[] { new AlertLine(AlertTypes.InstallationFailed, "k", "PC-0001", "7-Zip", "Exit code 1") };

        Assert.Equal("[ODM] Software installation failed: 7-Zip on PC-0001", AlertFormatter.Subject(alerts));
        Assert.Contains("Exit code 1", AlertFormatter.Body(alerts));
    }

    [Fact]
    public void Several_alerts_are_counted_in_the_subject_and_listed_in_the_body()
    {
        var alerts = new[]
        {
            new AlertLine(AlertTypes.DeviceSilent, "a", "PC-0001", "PC-0001", "No heartbeat"),
            new AlertLine(AlertTypes.CertificateExpiring, "b", "PC-0002", "Agent certificate", "Expires soon")
        };

        Assert.Equal("[ODM] 2 alerts", AlertFormatter.Subject(alerts));
        var body = AlertFormatter.Body(alerts);
        Assert.Contains("Device without heartbeat: PC-0001", body);
        Assert.Contains("Agent certificate expiring: Agent certificate on PC-0002", body);
    }

    [Fact]
    public void Line_breaks_in_details_do_not_break_the_list()
    {
        var alerts = new[] { new AlertLine(AlertTypes.InstallationFailed, "k", null, "App", "line one\nline two") };

        Assert.DoesNotContain("\n  line two", AlertFormatter.Body(alerts));
        Assert.Contains("line one line two", AlertFormatter.Body(alerts));
    }

    [Fact]
    public void Channels_are_configured_only_with_complete_settings()
    {
        Assert.False(new EmailOptions().Configured);
        Assert.False(new EmailOptions { Host = "smtp", From = "a@b.c" }.Configured);
        Assert.True(new EmailOptions { Host = "smtp", From = "a@b.c", To = ["x@y.z"] }.Configured);

        Assert.False(new WebhookOptions().Configured);
        Assert.False(new WebhookOptions { Url = "ftp://x" }.Configured);
        Assert.True(new WebhookOptions { Url = "https://hooks.example.org/x" }.Configured);
    }

    [Fact]
    public void Event_filter_defaults_to_all_and_is_case_insensitive()
    {
        Assert.True(new NotificationOptions().EventEnabled(AlertTypes.DeviceSilent));

        var only = new NotificationOptions { Events = ["Device-Silent"] };
        Assert.True(only.EventEnabled(AlertTypes.DeviceSilent));
        Assert.False(only.EventEnabled(AlertTypes.InstallationFailed));
    }
}
