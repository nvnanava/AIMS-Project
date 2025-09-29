namespace AIMS.ViewModels;

public sealed class PagedResult<T>
{
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    // Helpers so callers can avoid COUNT(*) when returning a single page with look-ahead.
    public static PagedResult<T> Empty() => new()
    {
        Total = 0,
        Page = 1,
        PageSize = 0,
        Items = Array.Empty<T>()
    };

    // Accept a list that may include one “look-ahead” item (pageSize + 1).
    public static PagedResult<T> From(IList<T> src, int pageSize)
    {
        var hasMore = src.Count > pageSize;
        var items = hasMore ? src.Take(pageSize).ToArray() : src.ToArray();
        return new PagedResult<T>
        {
            // -1 means “unknown total / skipped COUNT”
            Total = hasMore ? -1 : items.Length,
            Page = 1,
            PageSize = pageSize,
            Items = items
        };
    }
}
