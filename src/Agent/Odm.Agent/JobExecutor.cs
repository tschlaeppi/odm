using System.Threading.Channels;
using Odm.Agent.Jobs;

namespace Odm.Agent;

/// <summary>
/// Führt Aufträge unabhängig vom Heartbeat aus. Würde der Heartbeat auf eine lange Installation warten,
/// wäre das Gerät in der Zwischenzeit "offline" und der Server würde den Auftrag erneut ausliefern.
/// Aufträge laufen nacheinander (eine Installation zur Zeit).
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
    private readonly HashSet<Guid> _active = [];          // eingereiht oder in Ausführung
    private readonly List<JobResult> _results = [];       // fertig, aber noch nicht vom Server bestätigt

    /// <summary>Reiht einen Auftrag ein. Bereits bekannte Aufträge (erneute Auslieferung) werden ignoriert.</summary>
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

    /// <summary>Ergebnisse, die der Server erhalten hat, werden nicht erneut gesendet.</summary>
    public void Acknowledge(IReadOnlyCollection<JobResult> sent)
    {
        lock (_gate)
        {
            _results.RemoveAll(sent.Contains);
        }
    }

    /// <summary>Wartet bis zum Ablauf der Zeit oder bis ein Ergebnis vorliegt (dann soll sofort gemeldet werden).</summary>
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
            // Agent wird beendet
        }
    }

    private async Task<JobResult> RunAsync(ServerJob job, AgentState state, CancellationToken ct)
    {
        if (!_handlers.TryGetValue(job.Type, out var handler))
        {
            logger.LogWarning("Unbekannter Auftragstyp '{Type}' (Auftrag {JobId}).", job.Type, job.Id);
            return new JobResult(job.Id, false, $"Auftragstyp '{job.Type}' wird von diesem Agent nicht unterstützt.");
        }

        try
        {
            logger.LogInformation("Führe Auftrag {JobId} ({Type}) aus.", job.Id, job.Type);
            var outcome = await handler.HandleAsync(job, new JobContext(state, server, store), ct);
            logger.LogInformation("Auftrag {JobId} beendet: {Result}.", job.Id, outcome.Success ? "erfolgreich" : "fehlgeschlagen");
            return new JobResult(job.Id, outcome.Success, outcome.Output);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Auftrag {JobId} ist fehlgeschlagen.", job.Id);
            return new JobResult(job.Id, false, ex.Message);
        }
    }
}
