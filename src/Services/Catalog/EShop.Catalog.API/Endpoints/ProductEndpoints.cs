using MediatR;
using EShop.Catalog.Application.Products.Commands.CreateProduct;
using EShop.Catalog.Application.Products.Queries.GetProducts;

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

        // TODO: Implement GET /api/v1/products (with pagination, filtering, search)
        // group.MapGet("/", async (
        //     [AsParameters] GetProductsQuery query, 
        //     IMediator mediator) => { ... });

        // TODO: Implement GET /api/v1/products/{id}
        // group.MapGet("/{id:guid}", async (Guid id, IMediator mediator) => { ... });

        // TODO: Implement POST /api/v1/products (admin only)
        // group.MapPost("/", async (CreateProductCommand command, IMediator mediator) => { ... })
        //     .RequireAuthorization("Admin");

        // TODO: Implement PUT /api/v1/products/{id} (admin only)
        // group.MapPut("/{id:guid}", async (Guid id, UpdateProductCommand command, IMediator mediator) => { ... })
        //     .RequireAuthorization("Admin");

        // TODO: Implement DELETE /api/v1/products/{id} (admin only)
        // group.MapDelete("/{id:guid}", async (Guid id, IMediator mediator) => { ... })
        //     .RequireAuthorization("Admin");

        // TODO: Add output caching for GET requests
        // TODO: Add request validation
    }
}
