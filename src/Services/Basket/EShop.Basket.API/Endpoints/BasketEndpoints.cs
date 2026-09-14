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
/// Basket endpoints using Minimal API.
///
/// <para><b>One status mapping for every endpoint (Basket audit S8, M3/M4): <see cref="StatusFor"/>.</b> Each endpoint
/// used to pick its own — a fixed 400, or a substring test for "NotFound" — so Redis or Catalog being down answered 400,
/// the same product missing was 400 on one route and 404 on another, and gateway retries and 5xx alerting never saw an
/// outage. Every endpoint also passes the request's cancellation token on (L4), so a client that disconnects stops the
/// Catalog and Redis work it started.</para>
/// </summary>
public static class BasketEndpoints
{
    private static readonly HashSet<string> NotFoundCodes =
    [
        BasketErrors.BasketNotFound.Code,
        BasketErrors.ItemNotFound.Code,
        BasketErrors.ProductNotFound.Code
    ];

    /// <summary>"Not now — look again and retry" (D2, S4, S6).</summary>
    private static readonly HashSet<string> ConflictCodes =
    [
        BasketErrors.ConcurrentUpdate.Code,
        BasketErrors.CheckoutConflict.Code,
        BasketErrors.CheckoutAlreadyInProgress.Code,
        BasketErrors.InsufficientStock.Code,
        CheckoutRevalidationError.ErrorCode
    ];

    /// <summary>Redis or Catalog failed; the request itself was fine, so a retry may succeed (M3).</summary>
    private static readonly HashSet<string> UnavailableCodes =
    [
        BasketErrors.BasketOperationFailed.Code,
        BasketErrors.BasketPersistenceFailed.Code,
        BasketErrors.ProductVerificationFailed.Code
    ];

    public static void MapBasketEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/basket")
            .WithTags("Basket")
            // The owner for everything; an admin may only read (S10, D10). Decided by method, so a new write here is
            // owner-only by default.
            .RequireAuthorization(OwnerOrAdminReadRequirement.PolicyName);

        group.MapGet("/{userId}", async (string userId, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(new GetBasketQuery { UserId = userId }, cancellationToken);

            // D8: a user with no basket gets an empty one, so there is no 404 on this route.
            return result.Match(basket => Results.Ok(basket), error => Problem(error));
        })
        .WithName("GetBasket")
        .Produces<BasketDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{userId}/items", async (
            string userId,
            AddItemToBasketRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var command = new AddItemToBasketCommand
            {
                UserId = userId,
                ProductId = request.ProductId,
                Quantity = request.Quantity
            };

            var result = await mediator.Send(command, cancellationToken);

            return result.Match(_ => Results.NoContent(), error => Problem(error));
        })
        .WithName("AddItemToBasket")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPut("/{userId}/items/{productId:guid}", async (
            string userId,
            Guid productId,
            UpdateBasketItemQuantityRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var command = new UpdateBasketItemQuantityCommand
            {
                UserId = userId,
                ProductId = productId,
                Quantity = request.Quantity
            };

            var result = await mediator.Send(command, cancellationToken);

            return result.Match(_ => Results.NoContent(), error => Problem(error));
        })
        .WithName("UpdateBasketItemQuantity")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // Removing a product that is not in the basket is a 204: the item is not there afterwards either way.
        group.MapDelete("/{userId}/items/{productId:guid}", async (
            string userId,
            Guid productId,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(new RemoveBasketItemCommand
            {
                UserId = userId,
                ProductId = productId
            }, cancellationToken);

            return result.Match(_ => Results.NoContent(), error => Problem(error));
        })
        .WithName("RemoveBasketItem")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapDelete("/{userId}", async (string userId, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(new ClearBasketCommand { UserId = userId }, cancellationToken);

            return result.Match(_ => Results.NoContent(), error => Problem(error));
        })
        .WithName("ClearBasket")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{userId}/checkout", async (
            string userId,
            CheckoutBasketRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(new CheckoutBasketCommand
            {
                UserId = userId,
                ShippingAddress = request.ShippingAddress,
                PaymentMethod = request.PaymentMethod
            }, cancellationToken);

            return result.Match(
                checkoutId => Results.Ok(new { checkoutId }),
                error => error is CheckoutRevalidationError revalidation
                    ? new RevalidationProblemResult(revalidation)
                    : Problem(error));
        })
        .WithName("CheckoutBasket")
        .Produces<object>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>
    /// 404 for something named in the request that does not exist; 409 for a state that changed or is busy; 503 when
    /// Redis or Catalog failed; 400 for everything else (validation, an empty basket, a domain rule).
    /// </summary>
    private static int StatusFor(Error error)
        => NotFoundCodes.Contains(error.Code) ? StatusCodes.Status404NotFound
            : ConflictCodes.Contains(error.Code) ? StatusCodes.Status409Conflict
            : UnavailableCodes.Contains(error.Code) ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status400BadRequest;

    private static IResult Problem(Error error) => ProblemResults.For(error, StatusFor(error));

    /// <summary>
    /// Basket audit S6 (D6): the usual envelope, 409, plus a <c>lines</c> member naming each basket line that failed
    /// revalidation and why. <see cref="ProblemResults"/> carries no extensions, so this builds the same
    /// <see cref="EShopProblem"/> itself at execute time, when the HttpContext (and so the traceId) is known.
    /// </summary>
    private sealed class RevalidationProblemResult(CheckoutRevalidationError error) : IResult
    {
        public const string LinesKey = "lines";

        public Task ExecuteAsync(HttpContext httpContext)
        {
            var problem = EShopProblem.Create(httpContext, StatusCodes.Status409Conflict, error.Message, error.Code);
            problem.Extensions[LinesKey] = error.Lines;
            return EShopProblem.WriteAsync(httpContext, problem);
        }
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
