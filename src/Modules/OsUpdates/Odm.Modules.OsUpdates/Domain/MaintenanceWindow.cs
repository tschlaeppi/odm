using Odm.SharedKernel;

namespace Odm.Modules.OsUpdates.Domain;

[Flags]
public enum WeekDays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64
}

/// <summary>
/// Recurring weekly time window in a time zone in which updates may be installed.
/// Windows may extend past midnight. Maximum duration: 24 hours.
/// </summary>
public sealed class MaintenanceWindow : Entity
{
    public const int MinDurationMinutes = 15;
    public const int MaxDurationMinutes = 24 * 60;

    public string Name { get; private set; } = "";
    public WeekDays Days { get; private set; }

    /// <summary>Start in minutes after midnight (local time of the time zone), 0-1439.</summary>
    public int StartMinutes { get; private set; }

    public int DurationMinutes { get; private set; }

    /// <summary>IANA name, e.g. "Europe/Zurich". Daylight saving time is taken into account.</summary>
    public string TimeZoneId { get; private set; } = "UTC";

    public DateTimeOffset CreatedAt { get; private set; }

    private MaintenanceWindow() { }

    public static MaintenanceWindow Create(string name, WeekDays days, int startMinutes, int durationMinutes, string timeZoneId)
    {
        var window = new MaintenanceWindow { CreatedAt = DateTimeOffset.UtcNow };
        window.Update(name, days, startMinutes, durationMinutes, timeZoneId);
        return window;
    }

    public void Update(string name, WeekDays days, int startMinutes, int durationMinutes, string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        Days = days;
        StartMinutes = startMinutes;
        DurationMinutes = durationMinutes;
        TimeZoneId = timeZoneId;
    }

    public static bool IsValidTimeZone(string id) => TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);

    /// <summary>Is the point in time inside a window? If so: start and end of this occurrence (UTC).</summary>
    public bool TryGetOccurrence(DateTimeOffset now, out DateTimeOffset start, out DateTimeOffset end)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(now, zone).DateTime;

        // Check today and yesterday: a window that started yesterday can extend past midnight into today.
        for (var offset = 0; offset >= -1; offset--)
        {
            var date = localNow.Date.AddDays(offset);
            if (!Days.HasFlag(FlagFor(date.DayOfWeek)))
            {
                continue;
            }

            var startLocal = date.AddMinutes(StartMinutes);
            if (zone.IsInvalidTime(startLocal))
            {
                startLocal = startLocal.AddHours(1); // gap at the start of daylight saving time
            }

            var startUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(startLocal, zone), TimeSpan.Zero);
            var endUtc = startUtc.AddMinutes(DurationMinutes);
            if (now >= startUtc && now < endUtc)
            {
                start = startUtc;
                end = endUtc;
                return true;
            }
        }

        start = end = default;
        return false;
    }

    private static WeekDays FlagFor(DayOfWeek day) => (WeekDays)(1 << (((int)day + 6) % 7)); // Montag = 1 ... Sonntag = 64
}
