namespace RuinaoSoftwareWpf;

public sealed record PageRequest(
    int Offset = 0,
    int PageSize = 30,
    string? SearchText = null)
{
    public int SafeOffset => Math.Max(0, Offset);

    public int SafePageSize => Math.Clamp(PageSize, 1, 100);

    public string NormalizedSearchText => SearchText?.Trim() ?? string.Empty;
}

public sealed record PageResult<T>(
    IReadOnlyList<T> Items,
    bool HasMore,
    int? TotalCount = null);
