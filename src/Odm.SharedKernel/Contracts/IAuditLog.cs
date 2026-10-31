namespace Odm.SharedKernel.Contracts;

/// <summary>An event for the audit log. Never put secret values into any field.</summary>
/// <param name="Action">Machine-readable action, e.g. <c>auth.login</c>.</param>
/// <param name="Outcome"><c>success</c>, <c>failed</c>, <c>denied</c> or <c>error</c>.</param>
/// <param name="Actor">User name; when empty, the signed-in user of the current request is used.</param>
public sealed record AuditEvent(
    string Action,
    string Outcome,
    string? Actor = null,
    string? Role = null,
    string? Target = null,
    string? Details = null);

/// <summary>Appends entries to the audit log (implemented by the Audit module). Failures never reach the caller.</summary>
public interface IAuditLog
{
    Task RecordAsync(AuditEvent auditEvent, CancellationToken ct = default);
}
