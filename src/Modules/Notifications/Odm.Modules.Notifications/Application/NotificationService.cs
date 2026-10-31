using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Odm.Modules.Notifications.Domain;
using Odm.Modules.Notifications.Infrastructure;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.Notifications.Application;

public sealed class NotificationService(
    NotificationsDbContext db,
    IEnumerable<IAlertSource> sources,
    IEnumerable<IAlertChannel> channels,
    IDeviceDirectory devices,
    IOptions<NotificationOptions> options,
    ILogger<NotificationService> logger)
{
    /// <summary>
    /// Collects new alerts from all sources and sends them as one message. An alert is recorded as sent only after at
    /// least one channel accepted it; if every channel fails, the alerts are retried in the next round.
    /// </summary>
    public async Task<int> RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var settings = options.Value;
        var active = channels.Where(c => c.Configured).ToList();
        if (!settings.Enabled || active.Count == 0)
        {
            return 0;
        }

        var candidates = new List<AlertCandidate>();
        foreach (var source in sources)
        {
            try
            {
                candidates.AddRange((await source.GetAsync(now, ct)).Where(c => settings.EventEnabled(c.Type)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Alert source {Source} failed.", source.GetType().Name);
            }
        }

        if (candidates.Count == 0)
        {
            return 0;
        }

        var keys = candidates.Select(c => c.Key).Distinct().ToList();
        var known = (await db.Sent.AsNoTracking().Where(s => keys.Contains(s.Key)).Select(s => s.Key).ToListAsync(ct)).ToHashSet();
        var fresh = candidates
            .Where(c => !known.Contains(c.Key))
            .DistinctBy(c => c.Key)
            .Take(Math.Max(1, settings.MaxPerMessage))
            .ToList();
        if (fresh.Count == 0)
        {
            return 0;
        }

        var names = await devices.GetManyAsync(
            fresh.Where(c => c.DeviceId.HasValue).Select(c => c.DeviceId!.Value).ToList(), ct);
        var lines = fresh
            .Select(c => new AlertLine(
                c.Type, c.Key, c.DeviceId is { } id && names.TryGetValue(id, out var d) ? d.Name : null, c.Subject, c.Detail))
            .ToList();

        var accepted = await SendAsync(active, lines, ct);
        if (accepted.Count == 0)
        {
            return 0;
        }

        for (var i = 0; i < fresh.Count; i++)
        {
            db.Sent.Add(SentAlert.Create(
                fresh[i].Key, fresh[i].Type, fresh[i].DeviceId, lines[i].DeviceName, fresh[i].Subject, fresh[i].Detail,
                now, string.Join(',', accepted)));
        }

        await db.SaveChangesAsync(ct);
        return fresh.Count;
    }

    public IReadOnlyList<ChannelInfo> ChannelInfos() => channels.Select(c => new ChannelInfo(c.Name, c.Configured)).ToList();

    /// <summary>Sends to every configured channel and reports the result per channel (used by the test endpoint).</summary>
    public async Task<IReadOnlyList<ChannelResult>> SendTestAsync(CancellationToken ct)
    {
        var line = new AlertLine("test", "test", null, "Test message", "This is a test notification from Open Device Manager.");
        var results = new List<ChannelResult>();
        foreach (var channel in channels.Where(c => c.Configured))
        {
            try
            {
                await channel.SendAsync([line], ct);
                results.Add(new ChannelResult(channel.Name, true, null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Test notification through {Channel} failed.", channel.Name);
                results.Add(new ChannelResult(channel.Name, false, ex.Message));
            }
        }

        return results;
    }

    private async Task<List<string>> SendAsync(List<IAlertChannel> active, IReadOnlyCollection<AlertLine> lines, CancellationToken ct)
    {
        var accepted = new List<string>();
        foreach (var channel in active)
        {
            try
            {
                await channel.SendAsync(lines, ct);
                accepted.Add(channel.Name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Sending {Count} alerts through {Channel} failed.", lines.Count, channel.Name);
            }
        }

        return accepted;
    }
}
