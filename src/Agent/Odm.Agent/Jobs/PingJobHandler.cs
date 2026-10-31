namespace Odm.Agent.Jobs;

/// <summary>Test job: checks the whole path server → agent → server.</summary>
public sealed class PingJobHandler : IJobHandler
{
    public string Type => "ping";

    public Task<JobOutcome> HandleAsync(ServerJob job, JobContext context, CancellationToken ct) =>
        Task.FromResult(new JobOutcome(true, $"pong von {Environment.MachineName} um {DateTimeOffset.UtcNow:O}"));
}
