using System.Threading.Channels;
using Odm.Agent.Jobs;

namespace Odm.Agent;

/// <summary>
/// Runs jobs independently of the heartbeat. If the heartbeat waited for a long installation,
/// the device would be "offline" in the meantime and the server would deliver the job again.
/// Jobs run one after another (one installation at a time).
/// </summary>
public sealed class JobExecutor(
    ServerClient server,
    AgentStateStore store,
    IEnumerable<IJobHandler> handlers,
    ILogger<JobExecutor> logger) : BackgroundService
{
    private readonly Channel<(ServerJob Job, AgentState State)> _queue = Channel.CreateUnbounded<(ServerJob, AgentState)>();
    private readonly Dictionary<string, IJobHandler> _handlers = handlers.ToDictionary(h => h.Type);
    private readonly SemaphoreSlim _signal = new(0);
    private readonly object _gate = new();
    private readonly HashSet<Guid> _active = [];          // queued or running
    private readonly List<JobResult> _results = [];       // finished, but not yet acknowledged by the server

    /// <summary>Queues a job. Jobs that are already known (delivered again) are ignored.</summary>
    public bool Enqueue(ServerJob job, AgentState state)
    {
        lock (_gate)
        {
            if (_results.Any(r => r.JobId == job.Id) || !_active.Add(job.Id))
            {
                return false;
            }
        }

        _queue.Writer.TryWrite((job, state));
        return true;
    }

    public IReadOnlyList<Guid> ActiveJobIds()
    {
        lock (_gate)
        {
            return _active.ToList();
        }
    }

    public IReadOnlyList<JobResult> PendingResults()
    {
        lock (_gate)
        {
            return _results.ToList();
        }
    }

    /// <summary>Results that the server has received are not sent again.</summary>
    public void Acknowledge(IReadOnlyCollection<JobResult> sent)
    {
        lock (_gate)
        {
            _results.RemoveAll(sent.Contains);
        }
    }

    /// <summary>Waits until the time has elapsed or a result is available (then it should be reported immediately).</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        if (await _signal.WaitAsync(timeout, ct))
        {
            while (_signal.CurrentCount > 0)
            {
                await _signal.WaitAsync(0, ct);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var (job, state) in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                var result = await RunAsync(job, state, stoppingToken);
                lock (_gate)
                {
                    _results.Add(result);
                    _active.Remove(job.Id);
                }

                _signal.Release();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // agent is shutting down
        }
    }

    private async Task<JobResult> RunAsync(ServerJob job, AgentState state, CancellationToken ct)
    {
        if (!_handlers.TryGetValue(job.Type, out var handler))
        {
            logger.LogWarning("Unknown job type '{Type}' (job {JobId}).", job.Type, job.Id);
            return new JobResult(job.Id, false, $"Job type '{job.Type}' is not supported by this agent.");
        }

        try
        {
            logger.LogInformation("Running job {JobId} ({Type}).", job.Id, job.Type);
            var outcome = await handler.HandleAsync(job, new JobContext(state, server, store), ct);
            logger.LogInformation("Job {JobId} finished: {Result}.", job.Id, outcome.Success ? "succeeded" : "failed");
            return new JobResult(job.Id, outcome.Success, outcome.Output);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Job {JobId} failed.", job.Id);
            return new JobResult(job.Id, false, ex.Message);
        }
    }
}
