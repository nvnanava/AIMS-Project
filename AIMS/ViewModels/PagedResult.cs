namespace AIMS.ViewModels;

public sealed class PagedResult<T>
{
    // Exact total when >= 0; -1 means “unknown/skip-count” (look-ahead mode).
    public int Total { get; init; }

    // 1-based page index
    public int Page { get; init; }

    public int PageSize { get; init; }
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    public bool HasExactTotal => Total >= 0;

    public static PagedResult<T> Empty(int pageSize = 0) => new()
    {
        Total = 0,
        Page = 1,
        PageSize = pageSize,
        Items = Array.Empty<T>()
    };
}
