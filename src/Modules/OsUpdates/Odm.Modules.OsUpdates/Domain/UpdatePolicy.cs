using Odm.SharedKernel;

namespace Odm.Modules.OsUpdates.Domain;

public enum UpdateMode
{
    /// <summary>Security and critical updates only.</summary>
    SecurityOnly = 0,

    /// <summary>All available updates.</summary>
    All = 1
}

public enum RebootBehavior
{
    Never = 0,

    /// <summary>Restart after updates, but only within the maintenance window.</summary>
    IfRequired = 1
}

/// <summary>What is installed and how restarts are handled. The "when" is determined by maintenance windows.</summary>
public sealed class UpdatePolicy : Entity
{
    public string Name { get; private set; } = "";
    public string? Description { get; private set; }
    public UpdateMode Mode { get; private set; }
    public RebootBehavior Reboot { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private UpdatePolicy() { }

    public static UpdatePolicy Create(string name, string? description, UpdateMode mode, RebootBehavior reboot)
    {
        var policy = new UpdatePolicy { CreatedAt = DateTimeOffset.UtcNow };
        policy.Update(name, description, mode, reboot);
        return policy;
    }

    public void Update(string name, string? description, UpdateMode mode, RebootBehavior reboot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Mode = mode;
        Reboot = reboot;
    }
}
