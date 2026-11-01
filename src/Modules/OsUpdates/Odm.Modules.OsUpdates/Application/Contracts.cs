namespace Odm.Modules.OsUpdates.Application;

public sealed record PolicyRequest(string? Name, string? Description, string? Mode, string? Reboot);

public sealed record PolicyDto(Guid Id, string Name, string? Description, string Mode, string Reboot);

public sealed record WindowRequest(string? Name, List<string>? Days, int StartMinutes, int DurationMinutes, string? TimeZoneId);

public sealed record WindowDto(Guid Id, string Name, IReadOnlyList<string> Days, int StartMinutes, int DurationMinutes, string TimeZoneId);

public sealed record AssignmentRequest(string? Scope, Guid ParentId, string? TargetKind, string? Target);

public sealed record AssignmentDto(Guid Id, string Scope, Guid ParentId, string TargetKind, string Target, string? TargetName);

public sealed record RunDto(
    Guid Id, Guid DeviceId, string? DeviceName, string? WindowName, DateTimeOffset OccurrenceStart,
    string Status, int? Installed, int? Failed, bool RebootRequired, bool Rebooting, string? Message,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

public sealed record HistoryItemDto(
    Guid RunId, Guid DeviceId, string? DeviceName, string Title, string Status, DateTimeOffset At);

public sealed record HistoryPageDto(int Total, IReadOnlyList<HistoryItemDto> Items);

/// <summary>Content of the "os-update" job (read by the agent).</summary>
public sealed record OsUpdatePayload(string Mode, bool AllowReboot, DateTimeOffset WindowEndsAt, int TimeoutMinutes);

/// <summary>Result that the agent reports as job output (JSON).</summary>
public sealed record OsUpdateOutput(
    int Installed, int Failed, IReadOnlyList<string>? Titles, bool RebootRequired, bool Rebooting, string? Message);
