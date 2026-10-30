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
/// Wiederkehrendes Wochen-Zeitfenster in einer Zeitzone, in dem Updates installiert werden dürfen.
/// Fenster dürfen über Mitternacht reichen. Maximale Dauer: 24 Stunden.
/// </summary>
public sealed class MaintenanceWindow : Entity
{
    public const int MinDurationMinutes = 15;
    public const int MaxDurationMinutes = 24 * 60;

    public string Name { get; private set; } = "";
    public WeekDays Days { get; private set; }

    /// <summary>Beginn in Minuten nach Mitternacht (Ortszeit der Zeitzone), 0-1439.</summary>
    public int StartMinutes { get; private set; }

    public int DurationMinutes { get; private set; }

    /// <summary>IANA-Name, z. B. "Europe/Zurich". Sommerzeit wird berücksichtigt.</summary>
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

    /// <summary>Liegt der Zeitpunkt in einem Fenster? Wenn ja: Beginn und Ende dieses Auftretens (UTC).</summary>
    public bool TryGetOccurrence(DateTimeOffset now, out DateTimeOffset start, out DateTimeOffset end)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(now, zone).DateTime;

        // Heute und gestern prüfen: ein gestern begonnenes Fenster kann über Mitternacht bis heute reichen.
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
                startLocal = startLocal.AddHours(1); // Lücke bei Beginn der Sommerzeit
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
