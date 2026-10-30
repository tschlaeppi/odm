using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Odm.Modules.OsUpdates.Infrastructure;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.OsUpdates.Application;

/// <summary>Übernimmt das vom Agent gemeldete Ergebnis eines Update-Laufs.</summary>
public sealed class UpdateRunListener(OsUpdatesDbContext db) : IJobResultListener
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task OnJobCompletedAsync(JobCompletion completion, CancellationToken ct)
    {
        if (completion.Type != AgentJobTypes.OsUpdate)
        {
            return;
        }

        var run = await db.Runs.SingleOrDefaultAsync(r => r.JobId == completion.JobId, ct);
        if (run is null)
        {
            return;
        }

        OsUpdateOutput? output = null;
        try
        {
            output = JsonSerializer.Deserialize<OsUpdateOutput>(completion.Output ?? "", Json);
        }
        catch (JsonException)
        {
            // Ausgabe ist kein JSON (z. B. Fehlermeldung des Agents): als Text übernehmen
        }

        var message = output?.Message ??
                      (output?.Titles is { Count: > 0 } titles ? string.Join("; ", titles) : completion.Output);

        run.Complete(
            completion.Success, output?.Installed, output?.Failed,
            output?.RebootRequired ?? false, output?.Rebooting ?? false, message, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
    }
}
