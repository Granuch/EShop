using MediatR;
using EShop.Basket.Application.Commands.AddItemToBasket;
using EShop.Basket.Application.Commands.ClearBasket;
using EShop.Basket.Application.Commands.CheckoutBasket;
using EShop.Basket.Application.Commands.RemoveBasketItem;
using EShop.Basket.Application.Commands.UpdateBasketItemQuantity;
using EShop.Basket.Application.Queries.GetBasket;
using EShop.Basket.API.Infrastructure.Security;
using EShop.Basket.Application.Common;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Infrastructure.Http;

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
            .RequireAuthorization("SameUserOrAdmin");

        group.MapGet("/{userId}", async (string userId, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetBasketQuery { UserId = userId });

            return result.Match(
                basket => basket is null
                    ? Results.NotFound()
                    : Results.Ok(basket),
                error => ProblemResults.For(error, StatusCodes.Status400BadRequest));
        })
        .WithName("GetBasket")
        .Produces<BasketDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{userId}/items", async (string userId, AddItemToBasketRequest request, IMediator mediator) =>
        {
            var command = new AddItemToBasketCommand
            {
                UserId = userId,
                ProductId = request.ProductId,
                Quantity = request.Quantity
            };

            var result = await mediator.Send(command);

            return result.Match(
                _ => Results.NoContent(),
                error => ProblemResults.For(error, ConflictOr(error, StatusCodes.Status400BadRequest)));
        })
        .WithName("AddItemToBasket")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{userId}/items/{productId:guid}", async (
            string userId,
            Guid productId,
            UpdateBasketItemQuantityRequest request,
            IMediator mediator) =>
        {
            var command = new UpdateBasketItemQuantityCommand
            {
                UserId = userId,
                ProductId = productId,
                Quantity = request.Quantity
            };

            var result = await mediator.Send(command);

            return result.Match(
                _ => Results.NoContent(),
                error => ProblemFromError(error.Code, error.Message));
        })
        .WithName("UpdateBasketItemQuantity")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{userId}/items/{productId:guid}", async (
            string userId,
            Guid productId,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new RemoveBasketItemCommand
            {
                UserId = userId,
                ProductId = productId
            });

            return result.Match(
                _ => Results.NoContent(),
                error => ProblemFromError(error.Code, error.Message));
        })
        .WithName("RemoveBasketItem")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{userId}", async (string userId, IMediator mediator) =>
        {
            var result = await mediator.Send(new ClearBasketCommand { UserId = userId });

            return result.Match(
                _ => Results.NoContent(),
                error => ProblemResults.For(error, StatusCodes.Status400BadRequest));
        })
        .WithName("ClearBasket")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{userId}/checkout", async (
            string userId,
            CheckoutBasketRequest request,
            IMediator mediator) =>
        {
            var result = await mediator.Send(new CheckoutBasketCommand
            {
                UserId = userId,
                ShippingAddress = request.ShippingAddress,
                PaymentMethod = request.PaymentMethod
            });

            return result.Match(
                checkoutId => Results.Ok(new { checkoutId }),
                error => ProblemResults.For(error, ConflictOr(error, StatusCodes.Status400BadRequest)));
        })
        .WithName("CheckoutBasket")
        .Produces<object>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict);
    }

    /// <summary>
    /// Basket audit D2 and S4. A basket that kept changing while a write was being saved, a basket that changed while
    /// it was checked out, and another checkout holding the lock all mean "not now — look again and retry", not a
    /// malformed request, so they are 409s.
    /// </summary>
    private static bool IsConflict(string errorCode)
        => errorCode == BasketErrors.ConcurrentUpdate.Code
           || errorCode == BasketErrors.CheckoutConflict.Code
           || errorCode == BasketErrors.CheckoutAlreadyInProgress.Code;

    private static int ConflictOr(Error error, int otherwise)
        => IsConflict(error.Code) ? StatusCodes.Status409Conflict : otherwise;

    private static IResult ProblemFromError(string errorCode, string errorMessage)
    {
        var statusCode = IsConflict(errorCode)
            ? StatusCodes.Status409Conflict
            : errorCode.Contains("NotFound", StringComparison.OrdinalIgnoreCase)
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;

        return ProblemResults.For(errorCode, errorMessage, statusCode);
    }
}

public record AddItemToBasketRequest(
    Guid ProductId,
    int Quantity,
    string? ProductName = null,
    decimal? Price = null);

public record UpdateBasketItemQuantityRequest(int Quantity);

/// <summary>
/// <c>shippingAddress</c> is an object — street, city, state, zipCode, country (ISO alpha-2). It was a
/// single free-text string until Ordering audit C2; a body that still sends a string fails to bind (400).
/// </summary>
public record CheckoutBasketRequest(CheckoutAddress? ShippingAddress, string PaymentMethod);
