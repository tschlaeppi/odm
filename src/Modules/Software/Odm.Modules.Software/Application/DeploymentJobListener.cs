using Microsoft.EntityFrameworkCore;
using Odm.Modules.Software.Domain;
using Odm.Modules.Software.Infrastructure;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.Software.Application;

/// <summary>Takes over the result of an installation job reported by the agent.</summary>
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

        // If a newer version has been requested in the meantime, the reconciliation should pick that up immediately.
        trigger.Request();
    }
}
