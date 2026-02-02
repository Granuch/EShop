using EShop.Catalog.Application.Categories.Commands.CreateCategory;
using EShop.Catalog.Application.Categories.Queries.GetCategoryById;
using EShop.Catalog.Application.Products.Queries.GetProductByCategory;
using MediatR;

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

        // TODO: Implement GET /api/v1/categories (hierarchical structure)
        // group.MapGet("/", async (IMediator mediator) => { ... });

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
        group.MapPut("/categories", async (CreateCategoryCommand command, IMediator mediator) =>
        {
            var result = await mediator.Send(command);
            
            return Results.Created($"/api/v1/categories", result);
        });
        // TODO: Implement PUT /api/v1/categories/{id} (admin only)
        // TODO: Implement DELETE /api/v1/categories/{id} (admin only)
    }
}
