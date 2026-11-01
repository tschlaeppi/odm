using Odm.SharedKernel;

namespace Odm.Modules.OsUpdates.Domain;

/// <summary>One update (Windows update title or Linux package) that was part of an update run.</summary>
public sealed class UpdateRunItem : Entity
{
    public const int MaxTitleLength = 300;

    /// <summary>Upper bound per run so a huge upgrade cannot blow up the table.</summary>
    public const int MaxPerRun = 1000;

    public Guid RunId { get; private set; }
    public string Title { get; private set; } = "";

    private UpdateRunItem() { }

    public static UpdateRunItem Create(Guid runId, string title) => new()
    {
        RunId = runId,
        Title = title.Length > MaxTitleLength ? title[..MaxTitleLength] : title
    };
}
