using MediatR;
using EShop.BuildingBlocks.Infrastructure.Http;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Catalog.Application.Products.Commands.AddProductAttribute;
using EShop.Catalog.Application.Products.Commands.AddProductImage;
using EShop.Catalog.Application.Products.Commands.ClearProductDiscount;
using EShop.Catalog.Application.Products.Commands.CreateProduct;
using EShop.Catalog.Application.Products.Commands.PublishProduct;
using EShop.Catalog.Application.Products.Commands.SetProductDiscount;
using EShop.Catalog.Application.Products.Commands.UnpublishProduct;
using EShop.Catalog.Application.Products.Commands.DeleteProduct;
using EShop.Catalog.Application.Products.Commands.RemoveProductImage;
using EShop.Catalog.Application.Products.Commands.SetMainProductImage;
using EShop.Catalog.Application.Products.Commands.UpdateProduct;
using EShop.Catalog.Application.Products.Queries.GetNewestProducts;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using EShop.Catalog.Application.Products.Queries.GetProductsById;
using Microsoft.AspNetCore.RateLimiting;

namespace EShop.Catalog.API.Endpoints;

/// <summary>
/// Product endpoints using Minimal API.
/// Caching is handled by CachingBehavior in the MediatR pipeline via ICacheableQuery.
/// Cache invalidation is handled by CacheInvalidationBehavior via ICacheInvalidatingCommand.
/// </summary>
public static class ProductEndpoints
{
    /// <summary>
    /// Maps a failed Result to a problem response. A read or sub-resource endpoint can fail two
    /// ways: the product or image genuinely does not exist (404), or the request was rejected by
    /// ValidationBehavior, which surfaces as a "Validation.Failed" Result error rather than an
    /// exception (400). Mapping every error to one status gets one of those cases wrong.
    ///
    /// Used by GET /{id}, the image/attribute/publish/discount sub-resource endpoints, and
    /// CategoryEndpoints' GET /{id}/products (hence internal). POST/PUT still hard-code 400 and
    /// DELETE 404 — they have the same latent issue, left alone here because changing their codes
    /// would alter existing contract behaviour (e.g. Product.SkuConflict).
    /// </summary>
    internal static IResult ProblemForError(Error error)
        => ProblemResults.For(
            error,
            error.Code.EndsWith(".NotFound", StringComparison.Ordinal)
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest);

    /// <summary>
    /// D1 / H5a. Whether this caller may see unpublished (Draft) products — admins only.
    ///
    /// <para>
    /// This is the single place the decision is made, and it is made from the authenticated
    /// principal rather than from anything the client can send. The read endpoints are anonymous,
    /// so an unauthenticated request simply yields false; <c>RequireRole("Admin")</c> is the same
    /// check the write endpoints' authorization policy performs.
    /// </para>
    /// </summary>
    private static bool CanSeeUnpublished(HttpContext http) => http.User.IsInRole("Admin");

    public static void MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/products")
            .WithTags("Products");

        // GET /api/v1/products (with pagination, filtering, search)
        group.MapGet("/", async ([AsParameters] GetProductsQuery query, HttpContext http, IMediator mediator) =>
        {
            // D1 / H5a. The visibility flag is set HERE, from the caller's role, and overwrites
            // whatever was bound. GetProductsQuery is an [AsParameters] record, so every public
            // property is a query-string parameter — without this line `?IncludeUnpublished=true`
            // would hand any anonymous caller the unpublished catalog.
            var result = await mediator.Send(query with { IncludeUnpublished = CanSeeUnpublished(http) });

            return result.Match(
                value => Results.Ok(value),
                error => ProblemResults.For(error, StatusCodes.Status400BadRequest));
        })
        .WithName("GetProducts")
        .RequireRateLimiting("search")
        .Produces<PagedResult<ProductDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // GET /api/v1/products/newest (keyset pagination, newest first — H4)
        group.MapGet("/newest", async ([AsParameters] GetNewestProductsQuery query, HttpContext http, IMediator mediator) =>
        {
            // D1 / H5a. Same overwrite as GET / above, for the same reason: this is an
            // [AsParameters] record, so IncludeUnpublished is otherwise a client-settable
            // query-string parameter.
            var result = await mediator.Send(query with { IncludeUnpublished = CanSeeUnpublished(http) });

            return result.Match(
                value => Results.Ok(value),
                error => ProblemResults.For(error, StatusCodes.Status400BadRequest));
        })
        .WithName("GetNewestProducts")
        .RequireRateLimiting("search")
        .Produces<CursorPagedResult<ProductDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // GET /api/v1/products/{id}
        group.MapGet("/{id:guid}", async (Guid id, HttpContext http, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetProductByIdQuery
            {
                ProductId = id,
                IncludeUnpublished = CanSeeUnpublished(http)
            });

            // Discriminates rather than mapping every error to 404: a Guid.Empty id is rejected
            // by ValidationBehavior as a "Validation.Failed" Result, which owes a 400. Mapping
            // it to 404 reported a malformed request as a missing product.
            return result.Match(
                value => Results.Ok(value),
                ProblemForError);
        })
        .WithName("GetProductById")
        .Produces<ProductDetailsDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // POST /api/v1/products (admin only)
        group.MapPost("/", async (CreateProductCommand command, IMediator mediator) =>
        {
            var result = await mediator.Send(command);

            return result.Match(
                value => Results.Created($"/api/v1/products/{value}", new CreatedResourceResponse(value)),
                error => ProblemResults.For(error, StatusCodes.Status400BadRequest));
        })
        .WithName("CreateProduct")
        .RequireAuthorization("Admin")
        .Produces<CreatedResourceResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // PUT /api/v1/products/{id} (admin only)
        group.MapPut("/{id:guid}", async (Guid id, UpdateProductCommand command, IMediator mediator) =>
        {
            if (id != command.ProductId)
                return ProblemResults.For(
                    "Validation.IdMismatch",
                    "Route ID does not match command ID.",
                    StatusCodes.Status400BadRequest);

            var result = await mediator.Send(command);

            return result.Match(
                () => Results.NoContent(),
                error => ProblemResults.For(error, StatusCodes.Status400BadRequest));
        })
        .WithName("UpdateProduct")
        .RequireAuthorization("Admin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // DELETE /api/v1/products/{id} (admin only)
        group.MapDelete("/{id:guid}", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new DeleteProductCommand { ProductId = id });

            return result.Match(
                () => Results.NoContent(),
                error => ProblemResults.For(error, StatusCodes.Status404NotFound));
        })
        .WithName("DeleteProduct")
        .RequireAuthorization("Admin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // POST /api/v1/products/{id}/publish (admin only)
        // D1 / H5a. Until this existed, Product.Publish() had no production caller, so every
        // product was permanently Draft — and because nothing filtered on Status, the public
        // catalog served nothing but drafts. Both halves had to land together: an endpoint without
        // the read filter changes nothing visible, and the filter without an endpoint hides the
        // entire catalog.
        group.MapPost("/{id:guid}/publish", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new PublishProductCommand { ProductId = id });

            return result.Match(
                () => Results.NoContent(),
                ProblemForError);
        })
        .WithName("PublishProduct")
        .RequireAuthorization("Admin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // POST /api/v1/products/{id}/unpublish (admin only)
        group.MapPost("/{id:guid}/unpublish", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new UnpublishProductCommand { ProductId = id });

            return result.Match(
                () => Results.NoContent(),
                ProblemForError);
        })
        .WithName("UnpublishProduct")
        .RequireAuthorization("Admin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // PUT /api/v1/products/{id}/discount (admin only)
        // H5b / D3. Until this existed, DiscountPrice had no mutator anywhere, so it was
        // permanently null — while still being projected into both DTOs and priced against by
        // Basket (`DiscountPrice ?? Price`), a dead branch in another bounded context.
        //
        // A sub-resource with its own PUT/DELETE rather than a field on UpdateProductCommand: the
        // update command carries Price and StockQuantity as required values, so folding an optional
        // discount in would need "omitted means leave it, null means clear it" — the ambiguity the
        // root guide records as BUG-09. Two verbs make the intent explicit in the request line.
        group.MapPut("/{id:guid}/discount", async (Guid id, SetProductDiscountCommand command, IMediator mediator) =>
        {
            // The route owns the product id, so the body never has to repeat it.
            var result = await mediator.Send(command with { ProductId = id });

            return result.Match(
                () => Results.NoContent(),
                ProblemForError);
        })
        .WithName("SetProductDiscount")
        .RequireAuthorization("Admin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // DELETE /api/v1/products/{id}/discount (admin only)
        group.MapDelete("/{id:guid}/discount", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new ClearProductDiscountCommand { ProductId = id });

            return result.Match(
                () => Results.NoContent(),
                ProblemForError);
        })
        .WithName("ClearProductDiscount")
        .RequireAuthorization("Admin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // POST /api/v1/products/{id}/images (admin only)
        group.MapPost("/{id:guid}/images", async (Guid id, AddProductImageCommand command, IMediator mediator) =>
        {
            // The route owns the product id, so the body never has to repeat it.
            var result = await mediator.Send(command with { ProductId = id });

            return result.Match(
                value => Results.Created($"/api/v1/products/{id}", new CreatedResourceResponse(value)),
                ProblemForError);
        })
        .WithName("AddProductImage")
        .RequireAuthorization("Admin")
        .Produces<CreatedResourceResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // DELETE /api/v1/products/{id}/images/{imageId} (admin only)
        group.MapDelete("/{id:guid}/images/{imageId:guid}", async (Guid id, Guid imageId, IMediator mediator) =>
        {
            var result = await mediator.Send(new RemoveProductImageCommand
            {
                ProductId = id,
                ImageId = imageId
            });

            return result.Match(
                () => Results.NoContent(),
                ProblemForError);
        })
        .WithName("RemoveProductImage")
        .RequireAuthorization("Admin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // PUT /api/v1/products/{id}/images/{imageId}/main (admin only)
        group.MapPut("/{id:guid}/images/{imageId:guid}/main", async (Guid id, Guid imageId, IMediator mediator) =>
        {
            var result = await mediator.Send(new SetMainProductImageCommand
            {
                ProductId = id,
                ImageId = imageId
            });

            return result.Match(
                () => Results.NoContent(),
                ProblemForError);
        })
        .WithName("SetMainProductImage")
        .RequireAuthorization("Admin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // POST /api/v1/products/{id}/attributes (admin only)
        group.MapPost("/{id:guid}/attributes", async (Guid id, AddProductAttributeCommand command, IMediator mediator) =>
        {
            // The route owns the product id, so the body never has to repeat it.
            var result = await mediator.Send(command with { ProductId = id });

            return result.Match(
                value => Results.Created($"/api/v1/products/{id}", new CreatedResourceResponse(value)),
                ProblemForError);
        })
        .WithName("AddProductAttribute")
        .RequireAuthorization("Admin")
        .Produces<CreatedResourceResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
