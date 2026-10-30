using Odm.Agent.Jobs;

namespace Odm.Agent.Repo;

/// <summary>Zusatzargumente eines Auftrags: zerlegt, Secrets eingesetzt, Secret-Werte für die Ausgabe-Bereinigung.</summary>
public sealed record ResolvedArguments(IReadOnlyList<string> Tokens, IReadOnlyCollection<string> SecretValues)
{
    /// <summary>
    /// Erst zerlegen, dann ersetzen: so bleibt ein Secret mit Leerzeichen oder Anführungszeichen ein einzelnes Argument.
    /// Secret-Werte werden erst jetzt beim Server abgeholt, nicht mit dem Auftrag ausgeliefert.
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
