using MediatR;

namespace EShop.Ordering.API.Endpoints;

/// <summary>
/// Order endpoints using Minimal API
/// </summary>
public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/orders")
            .WithTags("Orders")
            .RequireAuthorization();

        // TODO: Implement GET /api/v1/orders (user's order history with pagination)
        // group.MapGet("/", async (IMediator mediator, ClaimsPrincipal user) => { ... });

        // TODO: Implement GET /api/v1/orders/{id}
        // group.MapGet("/{id:guid}", async (Guid id, IMediator mediator) => { ... });

        // TODO: Implement POST /api/v1/orders/{id}/cancel
        // group.MapPost("/{id:guid}/cancel", async (
        //     Guid id, 
        //     CancelOrderCommand command, 
        //     IMediator mediator) => { ... });

        // Admin endpoints
        // TODO: Implement GET /api/v1/orders/admin/all (admin only, with filters)
        // group.MapGet("/admin/all", async (...) => { ... })
        //     .RequireAuthorization("Admin");

        // TODO: Implement PUT /api/v1/orders/{id}/ship (admin only)
        // group.MapPut("/{id:guid}/ship", async (...) => { ... })
        //     .RequireAuthorization("Admin");

        // TODO: Add authorization checks (user can only see their own orders)
    }
}
