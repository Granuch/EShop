namespace EShop.BuildingBlocks.Application.Pagination;

/// <summary>
/// Paginated result wrapper with offset-based pagination
/// </summary>
public class PagedResult<T>
{
    public IEnumerable<T> Items { get; init; } = Enumerable.Empty<T>();
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;

    public static PagedResult<T> Create(IEnumerable<T> items, int pageNumber, int pageSize, int totalCount)
    {
        return new PagedResult<T>
        {
            Items = items,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public static PagedResult<T> Empty(int pageSize = 10)
    {
        return new PagedResult<T>
        {
            Items = Enumerable.Empty<T>(),
            PageNumber = 1,
            PageSize = pageSize,
            TotalCount = 0
        };
    }
}

/// <summary>
/// Cursor-based paginated result for large datasets
/// More efficient than offset pagination for deep pages
/// </summary>
public class CursorPagedResult<T>
{
    public IEnumerable<T> Items { get; init; } = Enumerable.Empty<T>();
    public int PageSize { get; init; }
    public string? NextCursor { get; init; }
    public string? PreviousCursor { get; init; }
    public bool HasNextPage => !string.IsNullOrEmpty(NextCursor);
    public bool HasPreviousPage => !string.IsNullOrEmpty(PreviousCursor);

    public static CursorPagedResult<T> Create(
        IEnumerable<T> items,
        int pageSize,
        string? nextCursor = null,
        string? previousCursor = null)
    {
        return new CursorPagedResult<T>
        {
            Items = items,
            PageSize = pageSize,
            NextCursor = nextCursor,
            PreviousCursor = previousCursor
        };
    }
}

/// <summary>
/// Request parameters for cursor-based pagination
/// </summary>
public record CursorPaginationRequest
{
    public string? Cursor { get; init; }
    public int PageSize { get; init; } = 20;
    public bool Forward { get; init; } = true;
}
