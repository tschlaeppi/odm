using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Odm.Modules.Software.Domain;

public enum RolloutMode
{
    /// <summary>Stufen mit Prozentwerten, z. B. 20 % nach 5 Tagen, 40 % nach 10 Tagen, 100 % nach 15 Tagen.</summary>
    Percent = 0,

    /// <summary>Stufen mit Untergruppen: jede Untergruppe wird nach ihrer Wartezeit freigegeben.</summary>
    Groups = 1
}

/// <summary>Eine Stufe: ab <see cref="AfterDays"/> Tagen nach der Zuweisung gilt sie. Je nach Modus mit Prozentwert oder Untergruppe.</summary>
public sealed record RolloutStage(int AfterDays, int? Percent, Guid? GroupId);

/// <summary>
/// Tranchenplan einer Zuweisung. Bestimmt, ab wann ein Gerät die Software erhalten darf.
/// Die Freigabe wirkt nur auf Geräte ohne bestehende Installation: Wer das Paket schon hat, wird nicht ausgebremst.
/// </summary>
public sealed class RolloutPlan
{
    public const int MaxStages = 10;
    public const int MaxDays = 365;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public RolloutMode Mode { get; }
    public IReadOnlyList<RolloutStage> Stages { get; }

    private RolloutPlan(RolloutMode mode, IReadOnlyList<RolloutStage> stages)
    {
        Mode = mode;
        Stages = stages;
    }

    /// <summary>Prüft die Stufen und liefert den Plan oder eine Fehlermeldung.</summary>
    public static (RolloutPlan? Plan, string? Error) Create(RolloutMode mode, IEnumerable<RolloutStage> stages)
    {
        var list = stages.OrderBy(s => s.AfterDays).ToList();
        if (list.Count is 0 or > MaxStages)
        {
            return (null, $"Ein Tranchenplan braucht 1 bis {MaxStages} Stufen.");
        }

        if (list.Any(s => s.AfterDays is < 0 or > MaxDays))
        {
            return (null, $"The delay must be between 0 and {MaxDays} days.");
        }

        if (mode == RolloutMode.Percent)
        {
            if (list.Any(s => s.Percent is null or < 1 or > 100 || s.GroupId is not null))
            {
                return (null, "Jede Stufe braucht einen Prozentwert von 1 bis 100.");
            }

            if (list.Select(s => s.AfterDays).Distinct().Count() != list.Count ||
                list.Zip(list.Skip(1), (a, b) => b.Percent <= a.Percent).Any(x => x))
            {
                return (null, "Delay and percentage must increase from stage to stage.");
            }

            if (list[^1].Percent != 100)
            {
                return (null, "The last stage must reach 100 %.");
            }
        }
        else
        {
            if (list.Any(s => s.GroupId is null || s.GroupId == Guid.Empty || s.Percent is not null))
            {
                return (null, "Jede Stufe braucht eine Untergruppe.");
            }

            if (list.Select(s => s.GroupId).Distinct().Count() != list.Count)
            {
                return (null, "Jede Untergruppe darf nur einmal vorkommen.");
            }
        }

        return (new RolloutPlan(mode, list), null);
    }

    public static RolloutPlan? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var stored = JsonSerializer.Deserialize<Stored>(json, Json);
        if (stored?.Stages is null)
        {
            return null;
        }

        return Create(stored.Mode, stored.Stages).Plan;
    }

    public string ToJson() => JsonSerializer.Serialize(new Stored(Mode, Stages.ToList()), Json);

    /// <summary>Anzahl der Stufen, deren Wartezeit abgelaufen ist.</summary>
    public int StagesReached(TimeSpan elapsed) => Stages.Count(s => elapsed >= TimeSpan.FromDays(s.AfterDays));

    /// <summary>Aktuell freigegebener Anteil in Prozent (nur im Prozentmodus, sonst null).</summary>
    public int? ReleasedPercent(TimeSpan elapsed) =>
        Mode == RolloutMode.Percent
            ? Stages.Where(s => elapsed >= TimeSpan.FromDays(s.AfterDays)).Select(s => s.Percent ?? 0).DefaultIfEmpty(0).Max()
            : null;

    /// <summary>
    /// Darf das Gerät jetzt beginnen? Prozentmodus: stabiler Zufallswert 0–99 je Zuweisung und Gerät,
    /// so bleibt die Auswahl über alle Abgleichsläufe gleich und wächst nur. Untergruppenmodus: Mitglieder einer Untergruppe
    /// ab deren Wartezeit; Geräte ausserhalb aller Untergruppen erst nach der letzten Stufe.
    /// </summary>
    /// <param name="isMember">Prüft, ob ein Gerät zu einer Untergruppe gehört (nur im Untergruppenmodus gebraucht).</param>
    public bool IsReleased(Guid assignmentId, Guid deviceId, TimeSpan elapsed, Func<Guid, Guid, bool>? isMember = null)
    {
        if (Mode == RolloutMode.Percent)
        {
            return Bucket(assignmentId, deviceId) < (ReleasedPercent(elapsed) ?? 0);
        }

        var matched = false;
        foreach (var stage in Stages)
        {
            if (isMember is not null && isMember(stage.GroupId!.Value, deviceId))
            {
                matched = true;
                if (elapsed >= TimeSpan.FromDays(stage.AfterDays))
                {
                    return true;
                }
            }
        }

        return !matched && elapsed >= TimeSpan.FromDays(Stages[^1].AfterDays);
    }

    /// <summary>Stabiler Wert 0–99 aus Zuweisung und Gerät.</summary>
    public static int Bucket(Guid assignmentId, Guid deviceId)
    {
        Span<byte> input = stackalloc byte[32];
        assignmentId.TryWriteBytes(input[..16]);
        deviceId.TryWriteBytes(input[16..]);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return (int)(BitConverter.ToUInt32(hash[..4]) % 100);
    }

    private sealed record Stored(RolloutMode Mode, List<RolloutStage> Stages);
}
