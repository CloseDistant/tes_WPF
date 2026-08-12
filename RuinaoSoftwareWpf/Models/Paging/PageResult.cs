namespace RuinaoSoftwareWpf;

public sealed record PageResult<T>(IReadOnlyList<T> Items, bool HasMore, int? TotalCount = null);
