namespace AIMS.ViewModels;
public sealed class PagedResult<T>
{
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
}
