using Odm.Modules.Software.Application;

namespace Odm.Tests;

public class DependencyRulesTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid();

    private static ILookup<Guid, Guid> Edges(params (Guid From, Guid To)[] edges) =>
        edges.ToLookup(e => e.From, e => e.To);

    [Fact]
    public void Closure_contains_indirect_dependencies()
    {
        var closure = DependencyRules.Closure(A, Edges((A, B), (B, C)));
        Assert.Equal(2, closure.Count);
        Assert.Contains(B, closure);
        Assert.Contains(C, closure);
        Assert.DoesNotContain(A, closure);
    }

    [Fact]
    public void Closure_survives_cycles()
    {
        var closure = DependencyRules.Closure(A, Edges((A, B), (B, A)));
        Assert.Equal([B], closure.ToArray());
    }

    [Fact]
    public void Self_dependency_is_rejected() =>
        Assert.NotNull(DependencyRules.Validate(A, [A], new HashSet<Guid> { A }, Edges()));

    [Fact]
    public void Unknown_package_is_rejected() =>
        Assert.NotNull(DependencyRules.Validate(A, [B], new HashSet<Guid> { A }, Edges()));

    [Fact]
    public void Cycle_is_rejected()
    {
        // B already needs A: A must not need B.
        var error = DependencyRules.Validate(A, [B], new HashSet<Guid> { A, B }, Edges((B, A)));
        Assert.NotNull(error);
    }

    [Fact]
    public void Indirect_cycle_is_rejected()
    {
        var error = DependencyRules.Validate(A, [B], new HashSet<Guid> { A, B, C }, Edges((B, C), (C, A)));
        Assert.NotNull(error);
    }

    [Fact]
    public void Valid_dependencies_are_accepted() =>
        Assert.Null(DependencyRules.Validate(A, [B, C], new HashSet<Guid> { A, B, C, D }, Edges((B, D))));

    [Fact]
    public void Too_deep_nesting_is_rejected()
    {
        var chain = Enumerable.Range(0, 12).Select(_ => Guid.NewGuid()).ToList();
        var edges = Edges(chain.Zip(chain.Skip(1), (a, b) => (a, b)).ToArray());
        var all = chain.Append(A).ToHashSet();
        Assert.NotNull(DependencyRules.Validate(A, [chain[0]], all, edges));
    }
}
