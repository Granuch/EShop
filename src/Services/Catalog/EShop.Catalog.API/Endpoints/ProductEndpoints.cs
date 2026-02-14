using EShop.BuildingBlocks.Application;
using MediatR;
using EShop.Catalog.Application.Products.Commands.CreateProduct;
using EShop.Catalog.Application.Products.Commands.DeleteProduct;
using EShop.Catalog.Application.Products.Commands.UpdateProduct;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using EShop.Catalog.Application.Products.Queries.GetProductsById;
using Microsoft.AspNetCore.OutputCaching;

namespace EShop.Catalog.API.Endpoints;

/// <summary>
/// Product endpoints using Minimal API
/// </summary>
public static class ProductEndpoints
{
    public static void MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/products")
            .WithTags("Products");

        // Implement GET /api/v1/products (with pagination, filtering, search)
        group.MapGet("/", async (
            [AsParameters] GetProductsQuery query,
            IMediator mediator) =>
        {
            var result = await mediator.Send(query);
            
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        });

        // GET /api/v1/products/{id}
        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator) =>
        {
            var query = new GetProductByIdQuery
            {
                ProductId = id
            };

            var result = await mediator.Send(query);

            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound();
        }).CacheOutput(policy => policy.Expire(TimeSpan.FromMinutes(5)).Tag("products"));

        // POST /api/v1/products/create (admin only)
        group.MapPost("/create",
            async (CreateProductCommand command, IMediator mediator, IOutputCacheStore cacheStore) =>
            {
                var result = await mediator.Send(command);

                await cacheStore.EvictByTagAsync("products", CancellationToken.None);

                return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
            }).CacheOutput(policy =>
            policy.Expire(TimeSpan.FromMinutes(5)).Tag("products")
                .SetVaryByQuery("page", "pageSize", "search", "categoryId"));
            //.RequireAuthorization("Admin");

        // PUT /api/v1/products/{id} (admin only)
        group.MapPut("/update", async (UpdateProductCommand command, IMediator mediator, IOutputCacheStore  cacheStore) =>
        {
            var result = await mediator.Send(command);
            
            await cacheStore.EvictByTagAsync("products", CancellationToken.None);
            
            return result.IsSuccess ? Results.Ok() :  Results.BadRequest(result.Error);
        }).RequireAuthorization("Admin");

        // DELETE /api/v1/products/{id} (admin only)
        group.MapDelete("/{id:guid}", async (Guid id, IMediator mediator, IOutputCacheStore  cacheStore) =>
        {
            var deleteCommand = new DeleteProductCommand
            {
                ProductId = id
            };
            
            var result = await mediator.Send(deleteCommand);
            
            await cacheStore.EvictByTagAsync("products", CancellationToken.None);
            
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        }).RequireAuthorization("Admin");
        
        // TODO: Add request validation
    }
}
