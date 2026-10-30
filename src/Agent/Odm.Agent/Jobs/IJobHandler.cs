namespace Odm.Agent.Jobs;

public sealed record JobOutcome(bool Success, string? Output);

/// <summary>Was ein Auftrag zur Ausführung braucht (Zugangsdaten für Downloads, Arbeitsverzeichnis).</summary>
public sealed record JobContext(AgentState State, ServerClient Server, AgentStateStore Store);

/// <summary>
/// Führt einen Auftragstyp aus. Neue Auftragstypen (Repo-Pakete, OS-Updates) kommen als weitere Handler hinzu,
/// ohne Worker oder Executor zu ändern.
/// Handler sollten idempotent sein: ein Auftrag kann nach einem Neustart des Agents erneut ausgeliefert werden.
/// </summary>
public interface IJobHandler
{
    string Type { get; }

    Task<JobOutcome> HandleAsync(ServerJob job, JobContext context, CancellationToken ct);
}
