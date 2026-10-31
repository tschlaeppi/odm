namespace Odm.Modules.Software.Application;

/// <summary>Wakes the reconciliation up immediately (e.g. after a new assignment) instead of waiting for the next interval.</summary>
public sealed class DeploymentTrigger
{
    private readonly SemaphoreSlim _signal = new(0);

    public void Request()
    {
        if (_signal.CurrentCount == 0)
        {
            _signal.Release();
        }
    }

    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct) => await _signal.WaitAsync(timeout, ct);
}
