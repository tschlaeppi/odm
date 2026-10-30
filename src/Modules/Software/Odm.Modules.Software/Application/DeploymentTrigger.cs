namespace Odm.Modules.Software.Application;

/// <summary>Weckt den Abgleich sofort auf (z. B. nach neuer Zuweisung), statt bis zum nächsten Intervall zu warten.</summary>
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
