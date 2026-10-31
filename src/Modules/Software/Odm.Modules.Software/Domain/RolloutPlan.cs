using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Odm.Modules.Software.Domain;

public enum RolloutMode
{
    /// <summary>Stages with percentages, e.g. 20 % after 5 days, 40 % after 10 days, 100 % after 15 days.</summary>
    Percent = 0,

    /// <summary>Stages with subgroups: each subgroup is released after its waiting time.</summary>
    Groups = 1
}

/// <summary>A stage: applies from <see cref="AfterDays"/> days after the assignment. Depending on the mode with a percentage or a subgroup.</summary>
public sealed record RolloutStage(int AfterDays, int? Percent, Guid? GroupId);

/// <summary>
/// Tranche plan of an assignment. Determines from when a device may receive the software.
/// The release only affects devices without an existing installation: whoever already has the package is not held back.
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

    /// <summary>Validates the stages and returns the plan or an error message.</summary>
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

    /// <summary>Number of stages whose waiting time has elapsed.</summary>
    public int StagesReached(TimeSpan elapsed) => Stages.Count(s => elapsed >= TimeSpan.FromDays(s.AfterDays));

    /// <summary>Currently released share in percent (percent mode only, otherwise null).</summary>
    public int? ReleasedPercent(TimeSpan elapsed) =>
        Mode == RolloutMode.Percent
            ? Stages.Where(s => elapsed >= TimeSpan.FromDays(s.AfterDays)).Select(s => s.Percent ?? 0).DefaultIfEmpty(0).Max()
            : null;

    /// <summary>
    /// May the device start now? Percent mode: stable random value 0–99 per assignment and device,
    /// so the selection stays the same across all reconciliation runs and only grows. Subgroup mode: members of a subgroup
    /// after its waiting time; devices outside all subgroups only after the last stage.
    /// </summary>
    /// <param name="isMember">Checks whether a device belongs to a subgroup (only needed in subgroup mode).</param>
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

    /// <summary>Stable value 0–99 from assignment and device.</summary>
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
