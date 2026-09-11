using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Ordering.Application.Orders.Commands.AddOrderItem;
using EShop.Ordering.Application.Orders.Commands.CancelOrder;
using EShop.Ordering.Application.Orders.Commands.CreateOrder;
using EShop.Ordering.Application.Orders.Commands.RemoveOrderItem;
using EShop.Ordering.Application.Orders.Commands.ShipOrder;
using EShop.Ordering.Application.Orders.Queries.GetOrderById;
using EShop.Ordering.Application.Orders.Queries.GetOrders;
using EShop.Ordering.Application.Orders.Queries.GetOrdersByUser;
using EShop.Ordering.API.Infrastructure.Security;
using EShop.BuildingBlocks.Infrastructure.Http;

namespace EShop.Ordering.API.Endpoints;

/// <summary>
/// Order endpoints using Minimal API.
/// Caching is handled by CachingBehavior in the MediatR pipeline via ICacheableQuery.
/// Cache invalidation is handled by CacheInvalidationBehavior via ICacheInvalidatingCommand.
/// </summary>
public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/orders")
            .WithTags("Orders");

        // POST /api/v1/orders
        group.MapPost("/", async (CreateOrderCommand command, IMediator mediator, HttpContext httpContext) =>
        {
            if (!CreateOrderCommandResolver.TryResolve(httpContext, command, out var resolvedCommand, out var error))
            {
                return error!;
            }

            var result = await mediator.Send(resolvedCommand);

            return result.Match(
                value => Results.Created($"/api/v1/orders/{value}", new { id = value }),
                error => ProblemResults.For(error, StatusCodes.Status400BadRequest));
        })
        .WithName("CreateOrder")
        .RequireAuthorization()
        .Produces<object>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // GET /api/v1/orders/{id}
        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetOrderByIdQuery { OrderId = id });

            return result.Match(
                value => Results.Ok(value),
                error => ProblemForError(error));
        })
        .WithName("GetOrderById")
        .RequireAuthorization("OrderOwnerOrAdmin")
        .Produces<object>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // GET /api/v1/orders
        group.MapGet("/", async ([AsParameters] GetOrdersQuery query, IMediator mediator) =>
        {
            var result = await mediator.Send(query);

            return result.Match(
                value => Results.Ok(value),
                error => ProblemResults.For(error, StatusCodes.Status400BadRequest));
        })
        .WithName("GetOrders")
        .RequireAuthorization("Admin")
        .Produces<object>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // GET /api/v1/users/{userId}/orders
        app.MapGet("/api/v1/users/{userId}/orders", async (string userId, [AsParameters] GetOrdersByUserQuery query, IMediator mediator) =>
        {
            var request = query with { UserId = userId };
            var result = await mediator.Send(request);

            return result.Match(
                value => Results.Ok(value),
                error => ProblemResults.For(error, StatusCodes.Status400BadRequest));
        })
        .WithTags("Orders")
        .WithName("GetOrdersByUser")
        .RequireAuthorization("SameUserOrAdmin")
        .Produces<object>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // POST /api/v1/orders/{id}/items
        group.MapPost("/{id:guid}/items", async (Guid id, AddOrderItemCommand command, IMediator mediator) =>
        {
            if (id != command.OrderId)
                return ProblemResults.For(
                    "Validation.IdMismatch",
                    "Route ID does not match command ID.",
                    StatusCodes.Status400BadRequest);

            var result = await mediator.Send(command);

            return result.Match(
                () => Results.NoContent(),
                error => ProblemForError(error));
        })
        .WithName("AddOrderItem")
        .RequireAuthorization("OrderOwnerOrAdmin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // DELETE /api/v1/orders/{id}/items/{itemId}
        group.MapDelete("/{id:guid}/items/{itemId:guid}", async (Guid id, Guid itemId, IMediator mediator) =>
        {
            var result = await mediator.Send(new RemoveOrderItemCommand { OrderId = id, ItemId = itemId });

            return result.Match(
                () => Results.NoContent(),
                error => ProblemForError(error));
        })
        .WithName("RemoveOrderItem")
        .RequireAuthorization("OrderOwnerOrAdmin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // POST /api/v1/orders/{id}/cancel
        group.MapPost("/{id:guid}/cancel", async (Guid id, CancelOrderRequest request, IMediator mediator) =>
        {
            var result = await mediator.Send(new CancelOrderCommand { OrderId = id, Reason = request.Reason });

            return result.Match(
                () => Results.NoContent(),
                error => ProblemForError(error));
        })
        .WithName("CancelOrder")
        .RequireAuthorization("OrderOwnerOrAdmin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // POST /api/v1/orders/{id}/ship (admin only)
        group.MapPost("/{id:guid}/ship", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new ShipOrderCommand { OrderId = id });

            return result.Match(
                () => Results.NoContent(),
                error => ProblemForError(error));
        })
        .WithName("ShipOrder")
        .RequireAuthorization("Admin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// One mapping from a handler's <see cref="Error"/> to a status, for every endpoint that addresses
    /// an existing order. Every code ending in <c>.NotFound</c> is a 404; a state conflict (the order is
    /// past the point where the request applies) is a 409; anything else — including
    /// <c>Validation.Failed</c> — is a 400. Before this, each endpoint hard-coded one status, so a
    /// missing order came back as 400 from cancel and the item endpoints, and a validation failure as
    /// 404 from GET.
    /// </summary>
    internal static IResult ProblemForError(Error error) => ProblemResults.For(error, StatusFor(error.Code));

    internal static int StatusFor(string errorCode) => errorCode switch
    {
        _ when errorCode.EndsWith(".NotFound", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
        "Order.NotPaidYet" or "Order.NotModifiable" or "Order.NotCancellable" => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest
    };
}

public record CancelOrderRequest(string Reason);
