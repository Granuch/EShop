using EShop.BuildingBlocks.Application;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using MediatR;

namespace EShop.Catalog.Application.Products.Queries.ExportProducts;

/// <summary>
/// Every product matching the admin list's filters, for <c>GET /api/v1/products/export</c> (admin panel S16, endpoint
/// #48). The filter surface is <see cref="ProductFilterQuery"/>, shared with <see cref="GetProductsQuery"/>, so "export
/// what I am looking at" is the same set of rows by construction.
/// </summary>
/// <remarks>
/// <para>
/// <b>Admin-only, and it includes drafts.</b> The endpoint requires the <c>Admin</c> policy and the handler passes
/// <c>includeUnpublished: true</c>, the way the recycle-bin read sets its own flag — there is deliberately no bound
/// property that could carry the decision. Soft-deleted products are excluded by the global filter, as in the list.
/// </para>
/// <para>
/// <b>Refused above <see cref="MaxRows"/>, never truncated</b> — the same rule, and the same bound, as Payment's
/// accounting export. A silently shortened export is a wrong answer that looks exactly like a right one.
/// </para>
/// <para>
/// <b>Not cached.</b> One admin screen's download; a cache entry would only make a just-made edit missing from it.
/// </para>
/// </remarks>
public record ExportProductsQuery : ProductFilterQuery, IRequest<Result<IReadOnlyList<ProductDto>>>
{
    public const int MaxRows = 10_000;
}
