using MediatR;
using EShop.Basket.Application.Commands.AddItemToBasket;
using EShop.Basket.Application.Commands.ClearBasket;
using EShop.Basket.Application.Commands.CheckoutBasket;
using EShop.Basket.Application.Commands.RemoveBasketItem;
using EShop.Basket.Application.Commands.UpdateBasketItemQuantity;
using EShop.Basket.Application.Queries.GetBasket;
using EShop.Basket.API.Infrastructure.Security;

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
                error => Results.Problem(
                    detail: error.Message,
                    title: error.Code,
                    statusCode: StatusCodes.Status400BadRequest));
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
                ProductName = request.ProductName,
                Price = request.Price,
                Quantity = request.Quantity
            };

            var result = await mediator.Send(command);

            return result.Match(
                _ => Results.NoContent(),
                error => Results.Problem(
                    detail: error.Message,
                    title: error.Code,
                    statusCode: StatusCodes.Status400BadRequest));
        })
        .WithName("AddItemToBasket")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest);

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
        .ProducesProblem(StatusCodes.Status400BadRequest);

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
        .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapDelete("/{userId}", async (string userId, IMediator mediator) =>
        {
            var result = await mediator.Send(new ClearBasketCommand { UserId = userId });

            return result.Match(
                _ => Results.NoContent(),
                error => Results.Problem(
                    detail: error.Message,
                    title: error.Code,
                    statusCode: StatusCodes.Status400BadRequest));
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
                error => Results.Problem(
                    detail: error.Message,
                    title: error.Code,
                    statusCode: StatusCodes.Status400BadRequest));
        })
        .WithName("CheckoutBasket")
        .Produces<object>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static IResult ProblemFromError(string errorCode, string errorMessage)
    {
        var statusCode = errorCode.Contains("NotFound", StringComparison.OrdinalIgnoreCase)
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status400BadRequest;

        return Results.Problem(
            detail: errorMessage,
            title: errorCode,
            statusCode: statusCode);
    }
}

public record AddItemToBasketRequest(Guid ProductId, string ProductName, decimal Price, int Quantity);

public record UpdateBasketItemQuantityRequest(int Quantity);

public record CheckoutBasketRequest(string ShippingAddress, string PaymentMethod);
