using MediatR;
using EShop.Catalog.Application.Products.Commands.CreateProduct;
using EShop.Catalog.Application.Products.Commands.DeleteProduct;
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
                error => Results.BadRequest(new { error = error.Code, message = error.Message }));
        })
        .WithName("GetProducts")
        .RequireRateLimiting("search")
        .Produces<object>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        // GET /api/v1/products/{id}
        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetProductByIdQuery { ProductId = id });

            return result.Match(
                value => Results.Ok(value),
                error => Results.NotFound(new { error = error.Code, message = error.Message }));
        })
        .WithName("GetProductById")
        .Produces<object>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // POST /api/v1/products (admin only)
        group.MapPost("/", async (CreateProductCommand command, IMediator mediator) =>
        {
            var result = await mediator.Send(command);

            return result.Match(
                value => Results.Created($"/api/v1/products/{value}", new { id = value }),
                error => Results.BadRequest(new { error = error.Code, message = error.Message }));
        })
        .WithName("CreateProduct")
        .RequireAuthorization("Admin")
        .Produces<object>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        // PUT /api/v1/products/{id} (admin only)
        group.MapPut("/{id:guid}", async (Guid id, UpdateProductCommand command, IMediator mediator) =>
        {
            if (id != command.ProductId)
                return Results.BadRequest(new { error = "Validation.IdMismatch", message = "Route ID does not match command ID." });

            var result = await mediator.Send(command);

            return result.Match(
                () => Results.NoContent(),
                error => Results.BadRequest(new { error = error.Code, message = error.Message }));
        })
        .WithName("UpdateProduct")
        .RequireAuthorization("Admin")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest);

        // DELETE /api/v1/products/{id} (admin only)
        group.MapDelete("/{id:guid}", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new DeleteProductCommand { ProductId = id });

            return result.Match(
                () => Results.NoContent(),
                error => Results.NotFound(new { error = error.Code, message = error.Message }));
        })
        .WithName("DeleteProduct")
        .RequireAuthorization("Admin")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);
    }
}
