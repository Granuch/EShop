using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Catalog.Application.Products.Commands.AddProductAttribute;
using EShop.Catalog.Application.Products.Commands.AddProductImage;
using EShop.Catalog.Application.Products.Commands.CreateProduct;
using EShop.Catalog.Application.Products.Commands.DeleteProduct;
using EShop.Catalog.Application.Products.Commands.RemoveProductImage;
using EShop.Catalog.Application.Products.Commands.SetMainProductImage;
using EShop.Catalog.Application.Products.Commands.UpdateProduct;
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
    /// Used by GET /{id} and the four image/attribute sub-resource endpoints. POST/PUT still
    /// hard-code 400 and DELETE 404 — they have the same latent issue, left alone here because
    /// changing their codes would alter existing contract behaviour (e.g. Product.SkuConflict).
    /// </summary>
    private static IResult ProblemForError(Error error)
        => Results.Problem(
            detail: error.Message,
            title: error.Code,
            statusCode: error.Code.EndsWith(".NotFound", StringComparison.Ordinal)
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest);

    public static void MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/products")
            .WithTags("Products");

        // GET /api/v1/products (with pagination, filtering, search)
        group.MapGet("/", async ([AsParameters] GetProductsQuery query, IMediator mediator) =>
        {
            var result = await mediator.Send(query);

            return result.Match(
                value => Results.Ok(value),
                error => Results.Problem(
                    detail: error.Message,
                    title: error.Code,
                    statusCode: StatusCodes.Status400BadRequest));
        })
        .WithName("GetProducts")
        .RequireRateLimiting("search")
        .Produces<object>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // GET /api/v1/products/{id}
        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetProductByIdQuery { ProductId = id });

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
                value => Results.Created($"/api/v1/products/{value}", new { id = value }),
                error => Results.Problem(
                    detail: error.Message,
                    title: error.Code,
                    statusCode: StatusCodes.Status400BadRequest));
        })
        .WithName("CreateProduct")
        .RequireAuthorization("Admin")
        .Produces<object>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // PUT /api/v1/products/{id} (admin only)
        group.MapPut("/{id:guid}", async (Guid id, UpdateProductCommand command, IMediator mediator) =>
        {
            if (id != command.ProductId)
                return Results.Problem(
                    detail: "Route ID does not match command ID.",
                    title: "Validation.IdMismatch",
                    statusCode: StatusCodes.Status400BadRequest);

            var result = await mediator.Send(command);

            return result.Match(
                () => Results.NoContent(),
                error => Results.Problem(
                    detail: error.Message,
                    title: error.Code,
                    statusCode: StatusCodes.Status400BadRequest));
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
                error => Results.Problem(
                    detail: error.Message,
                    title: error.Code,
                    statusCode: StatusCodes.Status404NotFound));
        })
        .WithName("DeleteProduct")
        .RequireAuthorization("Admin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // POST /api/v1/products/{id}/images (admin only)
        group.MapPost("/{id:guid}/images", async (Guid id, AddProductImageCommand command, IMediator mediator) =>
        {
            // The route owns the product id, so the body never has to repeat it.
            var result = await mediator.Send(command with { ProductId = id });

            return result.Match(
                value => Results.Created($"/api/v1/products/{id}", new { id = value }),
                ProblemForError);
        })
        .WithName("AddProductImage")
        .RequireAuthorization("Admin")
        .Produces<object>(StatusCodes.Status201Created)
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
                value => Results.Created($"/api/v1/products/{id}", new { id = value }),
                ProblemForError);
        })
        .WithName("AddProductAttribute")
        .RequireAuthorization("Admin")
        .Produces<object>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
