using Odm.Agent.Jobs;

namespace Odm.Agent.Repo;

/// <summary>Additional arguments of a job: split, secrets substituted, secret values for output redaction.</summary>
public sealed record ResolvedArguments(IReadOnlyList<string> Tokens, IReadOnlyCollection<string> SecretValues)
{
    /// <summary>
    /// Split first, then substitute: this way a secret containing spaces or quotation marks stays a single argument.
    /// Secret values are only fetched from the server now, not delivered with the job.
    /// </summary>
    public static async Task<ResolvedArguments> ResolveAsync(
        string? template, ServerJob job, JobContext context, CancellationToken ct)
    {
        var tokens = ArgumentSplitter.Split(template);
        if (!tokens.Any(SecretSubstitution.HasReferences))
        {
            return new ResolvedArguments(tokens, []);
        }

        var secrets = await context.Server.ResolveSecretsAsync(context.State, job.Id, ct);
        var resolved = tokens.Select(t => SecretSubstitution.Apply(t, secrets)).ToList();
        return new ResolvedArguments(resolved, secrets.Values.ToList());
    }
}
