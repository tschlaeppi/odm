namespace Odm.SharedKernel;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total);
