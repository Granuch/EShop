namespace EShop.BuildingBlocks.Application.Pagination;

/// <summary>
/// Paginated result wrapper
/// </summary>
public class PagedResult<T>
{
    public IEnumerable<T> Items { get; init; } = Enumerable.Empty<T>();
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;

    // TODO: Add cursor-based pagination support
    // public string? NextCursor { get; init; }
    // public string? PreviousCursor { get; init; }
}
