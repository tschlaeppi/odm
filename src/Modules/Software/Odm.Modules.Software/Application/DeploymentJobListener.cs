using Microsoft.EntityFrameworkCore;
using Odm.Modules.Software.Domain;
using Odm.Modules.Software.Infrastructure;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.Software.Application;

/// <summary>Übernimmt das vom Agent gemeldete Ergebnis eines Installationsauftrags.</summary>
public sealed class DeploymentJobListener(SoftwareDbContext db, DeploymentTrigger trigger) : IJobResultListener
{
    public async Task OnJobCompletedAsync(JobCompletion completion, CancellationToken ct)
    {
        if (completion.Type is not (AgentJobTypes.InstallPsadt or AgentJobTypes.InstallRepo))
        {
            return;
        }

        var installation = await db.Installations.SingleOrDefaultAsync(i => i.JobId == completion.JobId, ct);
        if (installation is null)
        {
            return;
        }

        installation.Complete(completion.Success, completion.Output, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);

        // Wurde inzwischen eine neuere Version gewünscht, soll der Abgleich das sofort aufgreifen.
        trigger.Request();
    }
}
