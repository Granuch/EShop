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

        // TODO: Implement GET /api/v1/categories/{id}
        // group.MapGet("/{id:guid}", async (Guid id, IMediator mediator) => { ... });

        // TODO: Implement GET /api/v1/categories/{id}/products
        // group.MapGet("/{id:guid}/products", async (Guid id, IMediator mediator) => { ... });

        // TODO: Implement POST /api/v1/categories (admin only)
        // TODO: Implement PUT /api/v1/categories/{id} (admin only)
        // TODO: Implement DELETE /api/v1/categories/{id} (admin only)
    }
}
