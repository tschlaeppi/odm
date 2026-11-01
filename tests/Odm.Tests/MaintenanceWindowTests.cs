using Odm.Modules.OsUpdates.Domain;

namespace Odm.Tests;

/// <summary>
/// Maintenance windows work with time zones and daylight saving time. Reference data (2026):
/// Daylight saving time in Europe starts on 29 March (Sunday) and ends on 25 October; 31 October is a Saturday (UTC+1),
/// and so is 4 July (UTC+2).
/// </summary>
public class MaintenanceWindowTests
{
    private static DateTimeOffset Utc(string iso) => DateTimeOffset.Parse(iso, null, System.Globalization.DateTimeStyles.AssumeUniversal);

    private static MaintenanceWindow SaturdayNight() =>
        MaintenanceWindow.Create("Nacht", WeekDays.Saturday, startMinutes: 120, durationMinutes: 120, "Europe/Zurich");

    [Fact]
    public void Inside_window_returns_start_and_end_in_utc()
    {
        // Saturday 02:00-04:00 in Zurich (winter time, UTC+1) = 01:00-03:00 UTC
        var found = SaturdayNight().TryGetOccurrence(Utc("2026-10-31T01:30:00Z"), out var start, out var end);

        Assert.True(found);
        Assert.Equal(Utc("2026-10-31T01:00:00Z"), start);
        Assert.Equal(Utc("2026-10-31T03:00:00Z"), end);
    }

    [Theory]
    [InlineData("2026-10-31T00:59:59Z", false)] // one second before the start
    [InlineData("2026-10-31T01:00:00Z", true)]  // the start is included
    [InlineData("2026-10-31T02:59:59Z", true)]
    [InlineData("2026-10-31T03:00:00Z", false)] // the end is no longer included
    public void Boundaries_are_start_inclusive_and_end_exclusive(string now, bool expected)
    {
        Assert.Equal(expected, SaturdayNight().TryGetOccurrence(Utc(now), out _, out _));
    }

    [Fact]
    public void Other_weekday_is_outside_window()
    {
        // Sunday 02:30 local time; the Saturday window is long over
        Assert.False(SaturdayNight().TryGetOccurrence(Utc("2026-11-01T01:30:00Z"), out _, out _));
    }

    [Fact]
    public void Window_crossing_midnight_is_found_on_the_following_day()
    {
        // Friday 23:00, two hours, UTC: extends to Saturday 01:00
        var window = MaintenanceWindow.Create("Mitternacht", WeekDays.Friday, 23 * 60, 120, "UTC");

        var found = window.TryGetOccurrence(Utc("2026-10-31T00:30:00Z"), out var start, out var end);

        Assert.True(found);
        Assert.Equal(Utc("2026-10-30T23:00:00Z"), start);
        Assert.Equal(Utc("2026-10-31T01:00:00Z"), end);
    }

    [Fact]
    public void Summer_time_shifts_the_utc_window()
    {
        // Saturday 4 July 2026: Zurich = UTC+2, 02:00 local time = 00:00 UTC
        var found = SaturdayNight().TryGetOccurrence(Utc("2026-07-04T00:30:00Z"), out var start, out var end);

        Assert.True(found);
        Assert.Equal(Utc("2026-07-04T00:00:00Z"), start);
        Assert.Equal(Utc("2026-07-04T02:00:00Z"), end);
    }

    [Fact]
    public void Start_time_in_the_spring_gap_is_moved_by_one_hour()
    {
        // 29 March 2026: 02:00 jumps to 03:00, 02:30 does not exist. The start becomes 03:30 local time = 01:30 UTC.
        var window = MaintenanceWindow.Create("Gap", WeekDays.Sunday, 150, 60, "Europe/Zurich");

        var found = window.TryGetOccurrence(Utc("2026-03-29T01:45:00Z"), out var start, out var end);

        Assert.True(found);
        Assert.Equal(Utc("2026-03-29T01:30:00Z"), start);
        Assert.Equal(Utc("2026-03-29T02:30:00Z"), end);
    }

    [Fact]
    public void Several_days_are_combined()
    {
        var window = MaintenanceWindow.Create("Wochenende", WeekDays.Saturday | WeekDays.Sunday, 0, 60, "UTC");

        Assert.True(window.TryGetOccurrence(Utc("2026-10-31T00:10:00Z"), out _, out _)); // Samstag
        Assert.True(window.TryGetOccurrence(Utc("2026-11-01T00:10:00Z"), out _, out _)); // Sonntag
        Assert.False(window.TryGetOccurrence(Utc("2026-11-02T00:10:00Z"), out _, out _)); // Montag
    }

    [Theory]
    [InlineData("Europe/Zurich", true)]
    [InlineData("UTC", true)]
    [InlineData("Mars/Olympus", false)]
    [InlineData("", false)]
    public void Time_zone_validation(string id, bool expected) =>
        Assert.Equal(expected, MaintenanceWindow.IsValidTimeZone(id));
}
