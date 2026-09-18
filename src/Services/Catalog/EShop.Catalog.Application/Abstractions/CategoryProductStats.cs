namespace EShop.Catalog.Application.Abstractions;

/// <summary>
/// Product-side counts for one category (Admin panel S5). Read in a single aggregate query by
/// <c>IProductQueryService.GetCategoryProductStatsAsync</c>.
/// </summary>
/// <remarks>
/// Soft-deleted products are excluded — the query runs under the <c>!p.IsDeleted</c> global filter,
/// matching every other read in the service. <see cref="TotalStock"/> is a <c>long</c> because it
/// sums an <c>int</c> column across arbitrarily many rows.
/// </remarks>
public sealed record CategoryProductStats(
    int ProductCount,
    int PublishedProductCount,
    long TotalStock,
    int OutOfStockCount);
