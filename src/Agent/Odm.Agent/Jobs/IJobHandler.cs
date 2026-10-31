namespace Odm.Agent.Jobs;

public sealed record JobOutcome(bool Success, string? Output);

/// <summary>What a job needs for execution (credentials for downloads, working directory).</summary>
public sealed record JobContext(AgentState State, ServerClient Server, AgentStateStore Store);

/// <summary>
/// Runs one job type. New job types (repository packages, OS updates) are added as further handlers
/// without changing the worker or the executor.
/// Handlers should be idempotent: a job can be delivered again after the agent restarts.
/// </summary>
public interface IJobHandler
{
    string Type { get; }

    Task<JobOutcome> HandleAsync(ServerJob job, JobContext context, CancellationToken ct);
}
