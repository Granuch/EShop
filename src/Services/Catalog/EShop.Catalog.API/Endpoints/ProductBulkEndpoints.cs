using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Infrastructure.Export;
using EShop.BuildingBlocks.Infrastructure.Http;
using EShop.Catalog.API.Infrastructure.Export;
using EShop.Catalog.Application.Products.Bulk;
using EShop.Catalog.Application.Products.Commands.BulkChangeProductCategory;
using EShop.Catalog.Application.Products.Commands.BulkDeleteProducts;
using EShop.Catalog.Application.Products.Commands.BulkPublishProducts;
using EShop.Catalog.Application.Products.Commands.BulkUnpublishProducts;
using EShop.Catalog.Application.Products.Commands.BulkUpdateProductPrices;
using EShop.Catalog.Application.Products.Commands.ImportProducts;
using EShop.Catalog.Application.Products.Queries.ExportProducts;
using MediatR;

namespace EShop.Catalog.API.Endpoints;

/// <summary>
/// Bulk actions, import and export on products (admin panel S16, endpoints #42–48; decision Q5a: synchronous,
/// hard-capped, per-row report).
/// </summary>
/// <remarks>
/// <para>
/// <b>A 200 carries the per-row report whatever the rows' outcomes</b>, even when every row was refused: the request was
/// understood and processed, and the report says what happened to each row. A non-2xx means the request as a whole was
/// refused — over the cap, malformed, a missing target category — and then <b>nothing</b> was changed.
/// </para>
/// <para>
/// <b>Two limits, both deliberate (risk A8).</b> The size of one request is capped by
/// <see cref="BulkProductLimits.MaxItemsPerRequest"/> and <see cref="ExportProductsQuery.MaxRows"/>; how often a caller
/// may send one is the <see cref="RateLimitPolicy"/> policy, partitioned per client like <c>search</c>. Without the second
/// a script could still run a thousand-product rewrite in a loop under the global limiter's 100 a minute.
/// </para>
/// <para>
/// <b>Admin role, like the rest of Catalog's admin surface.</b> Catalog has an <c>Admin</c> policy and every one of its
/// admin endpoints uses it; the permission migration happens service-wide, not one endpoint at a time.
/// </para>
/// </remarks>
public static class ProductBulkEndpoints
{
    /// <summary>The named rate-limit policy every endpoint here carries. Registered in <c>Program.cs</c>.</summary>
    public const string RateLimitPolicy = "bulk";

    public static IEndpointRouteBuilder MapProductBulkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/products")
            .WithTags("Products — Bulk")
            // On the group, so an endpoint added here cannot forget either.
            .RequireAuthorization("Admin")
            .RequireRateLimiting(RateLimitPolicy);

        group.MapPost("/bulk/publish", (BulkPublishProductsCommand command, IMediator mediator) => SendAsync(command, mediator))
            .WithName("BulkPublishProducts")
            .Produces<BulkProductReport>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/bulk/unpublish", (BulkUnpublishProductsCommand command, IMediator mediator) => SendAsync(command, mediator))
            .WithName("BulkUnpublishProducts")
            .Produces<BulkProductReport>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // POST, not DELETE with a body: a DELETE body has no defined meaning in HTTP, and proxies may drop it.
        group.MapPost("/bulk/delete", (BulkDeleteProductsCommand command, IMediator mediator) => SendAsync(command, mediator))
            .WithName("BulkDeleteProducts")
            .Produces<BulkProductReport>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/bulk/category", (BulkChangeProductCategoryCommand command, IMediator mediator) => SendAsync(command, mediator))
            .WithName("BulkChangeProductCategory")
            .Produces<BulkProductReport>(StatusCodes.Status200OK)
            // Category.NotFound is a 400, not a 404: the route exists, and it is the body that names a missing category.
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/bulk/price", (BulkUpdateProductPricesCommand command, IMediator mediator) => SendAsync(command, mediator))
            .WithName("BulkUpdateProductPrices")
            .Produces<BulkProductReport>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/import", (ImportProductsCommand command, IMediator mediator) => SendAsync(command, mediator))
            .WithName("ImportProducts")
            .Produces<ProductImportReport>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            // A SKU taken by a concurrent create between the check and the save reaches IX_Products_Sku; the whole import
            // rolls back and AddProductSkuConflict answers 409.
            .ProducesProblem(StatusCodes.Status409Conflict);

        // GET under a prefix whose reads are otherwise anonymous at the gateway: catalog-products-export-route carries
        // the Admin policy there, at an Order that beats catalog-products-read-route.
        group.MapGet("/export", async ([AsParameters] ExportProductsQuery query, IMediator mediator) =>
        {
            var result = await mediator.Send(query);

            return result.Match(
                products => Results.File(
                    SafeCsv.Encode(ProductCsvWriter.Write(products)),
                    "text/csv",
                    $"products-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv"),
                error => ProblemResults.For(error, StatusCodes.Status400BadRequest));
        })
        .WithName("ExportProducts")
        .Produces<string>(StatusCodes.Status200OK, "text/csv")
        .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> SendAsync<TReport>(IRequest<Result<TReport>> command, IMediator mediator)
    {
        var result = await mediator.Send(command);

        // Every whole-request refusal is a 400: validation (over the cap, an empty or repeated id) or a missing target
        // category, which the body names.
        return result.Match(
            report => Results.Ok(report),
            error => ProblemResults.For(error, StatusCodes.Status400BadRequest));
    }
}
