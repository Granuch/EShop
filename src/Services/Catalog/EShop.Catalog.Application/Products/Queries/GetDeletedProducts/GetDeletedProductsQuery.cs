using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using MediatR;

namespace EShop.Catalog.Application.Products.Queries.GetDeletedProducts;

/// <summary>
/// Admin-only list of soft-deleted products (Admin panel S4) — the recycle bin that makes
/// <c>POST /products/{id}/restore</c> usable, since nothing else in the system can show a deleted
/// product's id.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately not cached.</b> Every other list read implements <c>ICacheableQuery</c>, and
/// this one does not: its audience is one admin screen with negligible traffic, and the cost of
/// getting it wrong is high — the result set is defined by lifting a global query filter, so a key
/// that failed to distinguish it from the ordinary list would serve deleted products to the public
/// catalogue. Ordinary staleness is the other half: an admin who restores a product and reloads
/// the bin must not still see it there. If this ever needs caching, it needs its own family, not a
/// variant of <c>products:list</c>.
/// </para>
/// <para>
/// Every value type is nullable — <c>[AsParameters]</c> makes a non-nullable one a required
/// query-string parameter.
/// </para>
/// </remarks>
public record GetDeletedProductsQuery : IRequest<Result<PagedResult<ProductDto>>>
{
    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }
    public Guid? CategoryId { get; init; }
    public string? SearchTerm { get; init; }

    public int EffectivePageNumber => PageNumber ?? 1;
    public int EffectivePageSize => PageSize ?? 10;
}
