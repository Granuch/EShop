using MediatR;
using EShop.Ordering.Application.Orders.Commands.AddOrderItem;
using EShop.Ordering.Application.Orders.Commands.CancelOrder;
using EShop.Ordering.Application.Orders.Commands.CreateOrder;
using EShop.Ordering.Application.Orders.Commands.RemoveOrderItem;
using EShop.Ordering.Application.Orders.Commands.ShipOrder;
using EShop.Ordering.Application.Orders.Queries.GetOrderById;
using EShop.Ordering.Application.Orders.Queries.GetOrders;
using EShop.Ordering.Application.Orders.Queries.GetOrdersByUser;
using EShop.Ordering.API.Infrastructure.Security;

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
                error => Results.Problem(
                    detail: error.Message,
                    title: error.Code,
                    statusCode: StatusCodes.Status400BadRequest));
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
                error => Results.Problem(
                    detail: error.Message,
                    title: error.Code,
                    statusCode: StatusCodes.Status404NotFound));
        })
        .WithName("GetOrderById")
        .RequireAuthorization("OrderOwnerOrAdmin")
        .Produces<object>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // GET /api/v1/orders
        group.MapGet("/", async ([AsParameters] GetOrdersQuery query, IMediator mediator) =>
        {
            var result = await mediator.Send(query);

            return result.Match(
                value => Results.Ok(value),
                error => Results.Problem(
                    detail: error.Message,
                    title: error.Code,
                    statusCode: StatusCodes.Status400BadRequest));
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
                error => Results.Problem(
                    detail: error.Message,
                    title: error.Code,
                    statusCode: StatusCodes.Status400BadRequest));
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
                return Results.Problem(
                    detail: "Route ID does not match command ID.",
                    title: "Validation.IdMismatch",
                    statusCode: StatusCodes.Status400BadRequest);

            var result = await mediator.Send(command);

            return result.Match(
                () => Results.NoContent(),
                error => Results.Problem(
                    detail: error.Message,
                    title: error.Code,
                    statusCode: StatusCodes.Status400BadRequest));
        })
        .WithName("AddOrderItem")
        .RequireAuthorization("OrderOwnerOrAdmin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // DELETE /api/v1/orders/{id}/items/{itemId}
        group.MapDelete("/{id:guid}/items/{itemId:guid}", async (Guid id, Guid itemId, IMediator mediator) =>
        {
            var result = await mediator.Send(new RemoveOrderItemCommand { OrderId = id, ItemId = itemId });

            return result.Match(
                () => Results.NoContent(),
                error => Results.Problem(
                    detail: error.Message,
                    title: error.Code,
                    statusCode: StatusCodes.Status400BadRequest));
        })
        .WithName("RemoveOrderItem")
        .RequireAuthorization("OrderOwnerOrAdmin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // POST /api/v1/orders/{id}/cancel
        group.MapPost("/{id:guid}/cancel", async (Guid id, CancelOrderRequest request, IMediator mediator) =>
        {
            var result = await mediator.Send(new CancelOrderCommand { OrderId = id, Reason = request.Reason });

            return result.Match(
                () => Results.NoContent(),
                error => Results.Problem(
                    detail: error.Message,
                    title: error.Code,
                    statusCode: StatusCodes.Status400BadRequest));
        })
        .WithName("CancelOrder")
        .RequireAuthorization("OrderOwnerOrAdmin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // POST /api/v1/orders/{id}/ship (admin only)
        group.MapPost("/{id:guid}/ship", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new ShipOrderCommand { OrderId = id });

            return result.Match(
                () => Results.NoContent(),
                error => Results.Problem(
                    detail: error.Message,
                    title: error.Code,
                    statusCode: StatusCodes.Status400BadRequest));
        })
        .WithName("ShipOrder")
        .RequireAuthorization("Admin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}

public record CancelOrderRequest(string Reason);
