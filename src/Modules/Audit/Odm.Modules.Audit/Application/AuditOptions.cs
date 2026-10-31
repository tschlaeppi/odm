namespace Odm.Modules.Audit.Application;

public sealed class AuditOptions
{
    public const string SectionName = "Audit";

    /// <summary>Entries older than this many days are deleted daily. 0 = keep forever.</summary>
    public int RetentionDays { get; set; } = 365;
}

public sealed record AuditEntryDto(
    Guid Id, DateTimeOffset Timestamp, string Actor, string? Role, string Action, string? Target,
    string Outcome, int? StatusCode, string? IpAddress, string? Details);

public sealed record AuditPage(int Total, IReadOnlyList<AuditEntryDto> Items);
