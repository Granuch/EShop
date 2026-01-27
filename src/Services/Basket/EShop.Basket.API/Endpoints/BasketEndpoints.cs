using MediatR;
using EShop.Basket.Application.Commands.AddItemToBasket;
using EShop.Basket.Application.Commands.CheckoutBasket;
using EShop.Basket.Application.Queries.GetBasket;

namespace EShop.Basket.API.Endpoints;

/// <summary>
/// Basket endpoints using Minimal API
/// </summary>
public static class BasketEndpoints
{
    public static void MapBasketEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/basket")
            .WithTags("Basket")
            .RequireAuthorization();

        // TODO: Implement GET /api/v1/basket/{userId}
        // group.MapGet("/{userId}", async (string userId, IMediator mediator) => { ... });

        // TODO: Implement POST /api/v1/basket/{userId}/items
        // group.MapPost("/{userId}/items", async (
        //     string userId, 
        //     AddItemToBasketCommand command, 
        //     IMediator mediator) => { ... });

        // TODO: Implement PUT /api/v1/basket/{userId}/items/{productId}
        // group.MapPut("/{userId}/items/{productId:guid}", async (
        //     string userId, 
        //     Guid productId, 
        //     UpdateBasketItemCommand command, 
        //     IMediator mediator) => { ... });

        // TODO: Implement DELETE /api/v1/basket/{userId}/items/{productId}
        // group.MapDelete("/{userId}/items/{productId:guid}", async (...) => { ... });

        // TODO: Implement POST /api/v1/basket/{userId}/checkout
        // group.MapPost("/{userId}/checkout", async (
        //     string userId, 
        //     CheckoutBasketCommand command, 
        //     IMediator mediator) => { ... });

        // TODO: Add user validation (userId from token must match request)
    }
}
