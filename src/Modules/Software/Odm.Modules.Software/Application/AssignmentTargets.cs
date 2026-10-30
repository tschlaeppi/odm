using Odm.Modules.Software.Domain;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.Software.Application;

/// <summary>Gemeinsame Prüfung und Abbildung von Zielen und Tranchenplänen für Paket- und Profilzuweisungen.</summary>
public static class AssignmentTargets
{
    /// <summary>Prüft Zieltyp und Ziel (Gerät/Gruppe müssen existieren) und normalisiert die ID.</summary>
    public static async Task<(TargetKind Kind, string Target, string? ErrorField, string? Error)> ParseAsync(
        string? kindText, string? targetText, IDeviceDirectory directory, CancellationToken ct)
    {
        if (!Enum.TryParse<TargetKind>(kindText, true, out var kind) || !Enum.IsDefined(kind))
        {
            return (default, "", "targetKind", "Ungültiger Zieltyp.");
        }

        var target = targetText?.Trim() ?? "";
        if (target.Length is 0 or > 256)
        {
            return (kind, "", "target", "Ziel ist erforderlich.");
        }

        if (kind != TargetKind.User)
        {
            if (!Guid.TryParse(target, out var targetId))
            {
                return (kind, "", "target", "Ungültige ID.");
            }

            var exists = kind == TargetKind.Device
                ? await directory.GetAsync(targetId, ct) is not null
                : await directory.GroupExistsAsync(targetId, ct);
            if (!exists)
            {
                return (kind, "", "target", "Ziel nicht gefunden.");
            }

            target = targetId.ToString("D");
        }

        return (kind, target, null, null);
    }

    /// <summary>Wandelt die Eingabe in einen Plan um. Kein Plan (null) heisst: sofort an alle.</summary>
    public static async Task<(RolloutPlan? Plan, string? Error)> ParseRolloutAsync(
        RolloutDto? dto, IDeviceDirectory directory, CancellationToken ct)
    {
        if (dto is null || dto.Stages is null or { Count: 0 })
        {
            return (null, null);
        }

        if (!Enum.TryParse<RolloutMode>(dto.Mode, true, out var mode) || !Enum.IsDefined(mode))
        {
            return (null, "Ungültiger Tranchenmodus.");
        }

        var stages = dto.Stages.Select(s => new RolloutStage(s.AfterDays, s.Percent, s.GroupId)).ToList();
        var (plan, error) = RolloutPlan.Create(mode, stages);
        if (plan is null)
        {
            return (null, error);
        }

        if (mode == RolloutMode.Groups)
        {
            foreach (var stage in plan.Stages)
            {
                if (!await directory.GroupExistsAsync(stage.GroupId!.Value, ct))
                {
                    return (null, "Eine Untergruppe wurde nicht gefunden.");
                }
            }
        }

        return (plan, null);
    }

    public static RolloutDto? ToDto(RolloutPlan? plan) => plan is null
        ? null
        : new RolloutDto(plan.Mode.ToString(), plan.Stages.Select(s => new RolloutStageDto(s.AfterDays, s.Percent, s.GroupId)).ToList());

    public static RolloutStatusDto? ToStatus(RolloutPlan? plan, DateTimeOffset start, DateTimeOffset now) => plan is null
        ? null
        : new RolloutStatusDto(plan.StagesReached(now - start), plan.Stages.Count, plan.ReleasedPercent(now - start));
}
