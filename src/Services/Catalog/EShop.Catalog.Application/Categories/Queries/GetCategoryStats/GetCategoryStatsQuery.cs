using EShop.BuildingBlocks.Application;
using MediatR;

namespace EShop.Catalog.Application.Categories.Queries.GetCategoryStats;

/// <summary>
/// Per-category counts for the admin panel (Admin panel S5, endpoint #55).
/// </summary>
/// <remarks>
/// <b>Deliberately not cached</b>, like S4's two admin reads. It is one admin screen's worth of
/// traffic, and the numbers are exactly the kind an admin re-reads right after changing something —
/// a five-minute TTL here would mostly serve answers to questions the admin just made obsolete.
/// Caching it would also need its own family: it counts products, so it would have to be evicted by
/// every product write as well as every category write.
/// </remarks>
public record GetCategoryStatsQuery : IRequest<Result<CategoryStatsDto>>
{
    public Guid CategoryId { get; init; }
}

/// <param name="ProductCount">Live products directly in this category, published or not.</param>
/// <param name="PublishedProductCount">Of those, the ones an anonymous caller can see.</param>
/// <param name="TotalStock">Sum of <c>StockQuantity</c> over the live products counted above.</param>
/// <param name="OutOfStockCount">Live products in this category with zero stock.</param>
/// <param name="ChildCategoryCount">Live direct children.</param>
/// <remarks>
/// Counts are <b>direct members only, not the whole subtree</b>. A recursive count would need the
/// descendant closure on every call, and an admin reading a parent's row expects the number to
/// match what clicking into it shows. Deleted products are excluded throughout — they are invisible
/// everywhere else, and a stat that counted them would be the only place they appeared.
/// </remarks>
public sealed record CategoryStatsDto(
    Guid CategoryId,
    string CategoryName,
    int ProductCount,
    int PublishedProductCount,
    int TotalStock,
    int OutOfStockCount,
    int ChildCategoryCount);
