namespace Odm.Modules.Software.Application;

/// <summary>Rules for package dependencies: no self reference, no cycles, bounded size.</summary>
public static class DependencyRules
{
    public const int MaxDepth = 10;

    /// <summary>All packages a package needs, directly or indirectly (without the package itself).</summary>
    public static HashSet<Guid> Closure(Guid packageId, ILookup<Guid, Guid> edges)
    {
        var result = new HashSet<Guid>();
        var stack = new Stack<(Guid Id, int Depth)>();
        stack.Push((packageId, 0));
        while (stack.Count > 0)
        {
            var (current, depth) = stack.Pop();
            if (depth >= MaxDepth)
            {
                continue;
            }

            foreach (var next in edges[current])
            {
                if (next != packageId && result.Add(next))
                {
                    stack.Push((next, depth + 1));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Checks the new direct dependencies of a package against the existing ones of all other packages.
    /// Returns an error message or null.
    /// </summary>
    public static string? Validate(
        Guid packageId, IReadOnlyCollection<Guid> requested, ISet<Guid> existingPackages, ILookup<Guid, Guid> otherEdges)
    {
        if (requested.Count > Domain.PackageDependency.MaxPerPackage)
        {
            return $"At most {Domain.PackageDependency.MaxPerPackage} dependencies are allowed.";
        }

        foreach (var id in requested)
        {
            if (id == packageId)
            {
                return "A package cannot depend on itself.";
            }

            if (!existingPackages.Contains(id))
            {
                return "A required package was not found.";
            }

            // A cycle exists if the required package (indirectly) needs this package.
            if (Closure(id, otherEdges).Contains(packageId))
            {
                return "The dependencies would form a cycle.";
            }
        }

        // Nesting: the longest chain through this package must stay within the limit.
        var chain = 1 + requested.Select(id => Chain(id, otherEdges, 1)).DefaultIfEmpty(0).Max();
        return chain > MaxDepth ? $"Dependencies may be nested at most {MaxDepth} levels deep." : null;
    }

    /// <summary>Number of packages in the longest dependency chain starting at the package (cycles are excluded beforehand).</summary>
    private static int Chain(Guid id, ILookup<Guid, Guid> edges, int level) =>
        level > MaxDepth ? level : 1 + edges[id].Select(next => Chain(next, edges, level + 1)).DefaultIfEmpty(0).Max();
}
