using MediatR;
using EShop.Ordering.Application.Orders.Commands.AddOrderItem;
using EShop.Ordering.Application.Orders.Commands.CancelOrder;
using EShop.Ordering.Application.Orders.Commands.CreateOrder;
using EShop.Ordering.Application.Orders.Commands.RemoveOrderItem;
using EShop.Ordering.Application.Orders.Commands.ShipOrder;
using EShop.Ordering.Application.Orders.Queries.GetOrderById;
using EShop.Ordering.Application.Orders.Queries.GetOrders;
using EShop.Ordering.Application.Orders.Queries.GetOrdersByUser;
using System.Security.Claims;

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
            var user = httpContext.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return Results.Unauthorized();
            }

            var subjectId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                         ?? user.FindFirst("sub")?.Value
                         ?? user.FindFirst("uid")?.Value;
            var isAdmin = user.IsInRole("Admin");

            if (!isAdmin)
            {
                if (string.IsNullOrWhiteSpace(subjectId))
                {
                    return Results.Problem(
                        detail: "User identifier not found in authentication claims.",
                        title: "Unauthorized",
                        statusCode: StatusCodes.Status401Unauthorized);
                }

                if (!string.IsNullOrWhiteSpace(command.UserId) &&
                    !string.Equals(command.UserId, subjectId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Problem(
                        detail: "You are not allowed to create orders on behalf of other users.",
                        title: "Forbidden",
                        statusCode: StatusCodes.Status403Forbidden);
                }

                command = command with { UserId = subjectId };
            }
            else if (string.IsNullOrWhiteSpace(command.UserId))
            {
                return Results.Problem(
                    detail: "UserId is required for admin order creation.",
                    title: "Validation.UserIdRequired",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await mediator.Send(command);

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
        .RequireAuthorization()
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
        app.MapGet("/api/v1/users/{userId}/orders", async (string userId, IMediator mediator, HttpContext httpContext) =>
        {
            var user = httpContext.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return Results.Unauthorized();
            }

            if (!user.IsInRole("Admin"))
            {
                var subjectId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                             ?? user.FindFirst("sub")?.Value
                             ?? user.FindFirst("uid")?.Value;

                if (string.IsNullOrWhiteSpace(subjectId) ||
                    !string.Equals(subjectId, userId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Forbid();
                }
            }

            var result = await mediator.Send(new GetOrdersByUserQuery { UserId = userId });

            return result.Match(
                value => Results.Ok(value),
                error => Results.Problem(
                    detail: error.Message,
                    title: error.Code,
                    statusCode: StatusCodes.Status400BadRequest));
        })
        .WithTags("Orders")
        .WithName("GetOrdersByUser")
        .RequireAuthorization()
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
        .RequireAuthorization()
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
        .RequireAuthorization()
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
        .RequireAuthorization()
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
