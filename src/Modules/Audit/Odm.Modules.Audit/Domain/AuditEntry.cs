namespace Odm.Modules.Audit.Domain;

/// <summary>One immutable line of the audit log. The application offers no way to change or delete single entries.</summary>
public sealed class AuditEntry
{
    public const int MaxText = 512;

    public Guid Id { get; private set; }
    public DateTimeOffset Timestamp { get; private set; }

    /// <summary>User name, <c>agent:{id}</c> or <c>anonymous</c>.</summary>
    public string Actor { get; private set; } = "";
    public string? Role { get; private set; }

    /// <summary>E.g. <c>auth.login</c> or <c>DELETE software/packages/{id}</c>.</summary>
    public string Action { get; private set; } = "";

    /// <summary>The affected object, usually the request path including IDs.</summary>
    public string? Target { get; private set; }

    /// <summary><c>success</c>, <c>failed</c>, <c>denied</c> or <c>error</c>.</summary>
    public string Outcome { get; private set; } = "";
    public int? StatusCode { get; private set; }
    public string? IpAddress { get; private set; }
    public string? Details { get; private set; }

    private AuditEntry() { }

    public static AuditEntry Create(
        DateTimeOffset timestamp, string actor, string? role, string action, string? target,
        string outcome, int? statusCode, string? ipAddress, string? details) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = timestamp,
        Actor = Cut(actor) ?? "anonymous",
        Role = Cut(role, 32),
        Action = Cut(action) ?? "unknown",
        Target = Cut(target),
        Outcome = Cut(outcome, 16) ?? "success",
        StatusCode = statusCode,
        IpAddress = Cut(ipAddress, 64),
        Details = Cut(details, 1024)
    };

    private static string? Cut(string? value, int max = MaxText) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];
}
