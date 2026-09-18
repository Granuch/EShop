namespace EShop.Catalog.API.Endpoints;

/// <summary>
/// The body of <c>PATCH /api/v1/products/{id}/stock</c>: <c>{ "productId": "…", "stockQuantity": 42 }</c>
/// (Admin panel S4).
/// </summary>
/// <remarks>
/// A body rather than a 204 because a relative adjustment's whole point is that the caller does not
/// know the result — <c>?delta=-3</c> composes with whatever anyone else did. Returning it here is
/// what lets an admin UI update its display without a follow-up GET, which would reintroduce the
/// read-modify-write race the endpoint exists to remove. Named rather than anonymous, so the
/// OpenAPI document describes it (L26).
/// </remarks>
public sealed record ProductStockResponse(Guid ProductId, int StockQuantity);
