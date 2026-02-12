using EShop.Catalog.Application.Categories.Commands.CreateCategory;
using EShop.Catalog.Application.Categories.Commands.DeleteCategory;
using EShop.Catalog.Application.Categories.Commands.UpdateCategory;
using EShop.Catalog.Application.Categories.Queries.GetCategories;
using EShop.Catalog.Application.Categories.Queries.GetCategoryById;
using EShop.Catalog.Application.Products.Queries.GetProductByCategory;
using MediatR;
using Microsoft.AspNetCore.OutputCaching;

namespace EShop.Catalog.API.Endpoints;

/// <summary>
/// Category endpoints using Minimal API
/// </summary>
public static class CategoryEndpoints
{
    public static void MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/categories")
            .WithTags("Categories");

        // Implement GET /api/v1/categories (hierarchical structure)
        group.MapGet("/", async (IMediator mediator) =>
        {
            var result = await mediator.Send(new GetCategoriesQuery());
            
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).CacheOutput(p => p.Tag("categories"));

        // GET /api/v1/categories/{id}
        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator) =>
        {
            var query = new GetCategoryByIdQuery
            {
                Id = id
            };
            
            var result = await mediator.Send(query);
            
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound();
        });

        // GET /api/v1/categories/{id}/products
        group.MapGet("/{id:guid}/products", async (Guid id, IMediator mediator) =>
        {
            var query = new GetProductByCategoryQuery
            {
                CategoryId = id
            };
            
            var result = await mediator.Send(query);
            
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound();
        });

        // Implement POST /api/v1/categories (admin only)
        group.MapPost("/categories", async (CreateCategoryCommand command, IMediator mediator, IOutputCacheStore cache) =>
        {
            var result = await mediator.Send(command);
            await cache.EvictByTagAsync("categories", CancellationToken.None);
            
            return Results.Created($"/api/v1/categories", result);
        });
        // Implement PUT /api/v1/categories/{id} (admin only)
        group.MapPut("/{id:guid}", async (UpdateCategoryCommand command, IMediator mediator, IOutputCacheStore cache, Guid id) =>
        {
            command.Id = id;
            var result = await mediator.Send(command);
            await cache.EvictByTagAsync("categories", CancellationToken.None);
            
            return result.IsSuccess ? Results.Ok() :  Results.BadRequest(result.Error);
        });
        // DELETE /api/v1/categories/{id} (admin only)
        group.MapDelete("/{id:Guid}", async (Guid id, IMediator mediator, IOutputCacheStore cache) =>
        {
            var command = new DeleteCategoryCommand
            {
                Id = id
            };
            
            var result = await mediator.Send(command);
            await cache.EvictByTagAsync("categories", CancellationToken.None);
            return result.IsSuccess ? Results.Ok() : Results.NotFound();
        });
    }
}
