using EShop.BuildingBlocks.Application.Pagination;
using EShop.BuildingBlocks.Infrastructure.Http;
using EShop.Catalog.Application.Products.Queries.GetLowStockProducts;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using MediatR;

namespace EShop.Catalog.API.Endpoints;

/// <summary>
/// Catalog's operational reads, under <c>/api/v1/admin/catalog</c> (Admin panel S4, endpoint #49).
/// </summary>
/// <remarks>
/// <para>
/// A separate group rather than another route on <c>/api/v1/products</c>, because this is not a
/// view of the product resource — it is a dashboard answering "what needs attention", and the
/// admin-panel plan reserves <c>/api/v1/admin/{service}/**</c> for exactly that across every
/// service (G2, with siblings for users, audit and settings).
/// </para>
/// <para>
/// <b>A new path prefix is three coordinated changes, not one.</b> The endpoint here, the gateway
/// route <c>admin-catalog-route</c> carrying <c>AuthorizationPolicy: Admin</c> (G2), and the
/// <c>/api/v1/admin/catalog</c> prefix in <c>CatalogProxyGuardMiddleware</c> (G6). Miss the second
/// and the path is unroutable through the gateway; miss the third and it is proxied with no
/// request-body cap and leaks bare 502s. S1's <c>GatewayRouteAuthorizationTests</c> and
/// <c>ProxyGuardCoverageTests</c> fail if either is forgotten — that is what they are for.
/// </para>
/// </remarks>
public static class AdminCatalogEndpoints
{
    public static IEndpointRouteBuilder MapAdminCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/catalog")
            .WithTags("Admin — Catalog")
            // Applied to the group rather than per-endpoint: everything under /api/v1/admin is
            // admin-only by definition, and a per-endpoint policy is the thing someone forgets.
            .RequireAuthorization("Admin");

        // GET /api/v1/admin/catalog/low-stock?threshold=10
        group.MapGet("/low-stock", async ([AsParameters] GetLowStockProductsQuery query, IMediator mediator) =>
        {
            var result = await mediator.Send(query);

            return result.Match(
                value => Results.Ok(value),
                error => ProblemResults.For(error, StatusCodes.Status400BadRequest));
        })
        .WithName("GetLowStockProducts")
        .Produces<PagedResult<ProductDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }
}
