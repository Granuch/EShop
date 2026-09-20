using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Ordering.Application.Orders.Commands.AddOrderItem;
using EShop.Ordering.Application.Orders.Commands.AddOrderNote;
using EShop.Ordering.Application.Orders.Commands.CancelOrder;
using EShop.Ordering.Application.Orders.Commands.CreateOrder;
using EShop.Ordering.Application.Orders.Commands.DeliverOrder;
using EShop.Ordering.Application.Orders.Commands.RemoveOrderItem;
using EShop.Ordering.Application.Orders.Commands.ShipOrder;
using EShop.Ordering.Application.Orders.Commands.UpdateOrderItemQuantity;
using EShop.Ordering.Application.Orders.Commands.UpdateShippingAddress;
using EShop.Ordering.Application.Orders.Queries;
using EShop.Ordering.Application.Orders.Queries.GetOrderById;
using EShop.Ordering.Application.Orders.Queries.GetOrderNotes;
using EShop.Ordering.Application.Orders.Queries.GetOrders;
using EShop.Ordering.Application.Orders.Queries.GetOrderStats;
using EShop.Ordering.Application.Orders.Queries.GetOrderStatusHistory;
using EShop.Ordering.Application.Orders.Queries.GetOrdersByUser;
using EShop.Ordering.API.Infrastructure.Security;
using EShop.BuildingBlocks.Infrastructure.Http;

namespace EShop.Ordering.API.Endpoints;

/// <summary>
/// Order endpoints using Minimal API.
/// Caching is handled by CachingBehavior in the MediatR pipeline via ICacheableQuery.
/// Cache invalidation is handled by CacheInvalidationBehavior via ICacheInvalidatingCommand.
/// Every success response declares its type (audit L8); they were all <c>Produces&lt;object&gt;</c>, so the
/// OpenAPI document described no response shapes at all.
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
                value => Results.Created($"/api/v1/orders/{value}", new CreateOrderResponse(value)),
                error => ProblemForError(error));
        })
        .WithName("CreateOrder")
        .RequireAuthorization()
        .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

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
        .Produces<OrderDto>(StatusCodes.Status200OK)
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
        .Produces<PagedResult<OrderDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // GET /api/v1/orders/stats (admin only) — Admin panel S8, endpoint #63.
        // Mapped before "/{id:guid}" for readability only: the route constraint is what keeps "stats"
        // from being read as an order id, so the two cannot collide whatever the order here.
        group.MapGet("/stats", async ([AsParameters] GetOrderStatsQuery query, IMediator mediator) =>
        {
            var result = await mediator.Send(query);

            return result.Match(
                value => Results.Ok(value),
                error => ProblemForError(error));
        })
        .WithName("GetOrderStats")
        .RequireAuthorization("Admin")
        .Produces<OrderStatsDto>(StatusCodes.Status200OK)
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
        .Produces<PagedResult<OrderDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // POST /api/v1/orders/{id}/items
        // The route names the order (audit L8): the body's OrderId may be omitted, and is refused only when
        // it names a different order. It used to be required in both places.
        group.MapPost("/{id:guid}/items", async (Guid id, AddOrderItemCommand command, IMediator mediator) =>
        {
            if (command.OrderId != Guid.Empty && command.OrderId != id)
                return ProblemResults.For(
                    "Validation.IdMismatch",
                    "Route ID does not match command ID.",
                    StatusCodes.Status400BadRequest);

            var result = await mediator.Send(command with { OrderId = id });

            return result.Match(
                () => Results.NoContent(),
                error => ProblemForError(error));
        })
        .WithName("AddOrderItem")
        .RequireAuthorization("OrderOwnerOrAdmin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

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

        // PUT /api/v1/orders/{id}/items/{itemId} — Admin panel S8, endpoint #57.
        // OrderOwnerOrAdmin, like the add and remove beside it: the order is still Pending, so its
        // owner correcting a quantity is the ordinary case and an admin doing it is the admin case.
        group.MapPut("/{id:guid}/items/{itemId:guid}", async (
            Guid id, Guid itemId, UpdateOrderItemQuantityRequest request, IMediator mediator) =>
        {
            var result = await mediator.Send(new UpdateOrderItemQuantityCommand
            {
                OrderId = id,
                ItemId = itemId,
                Quantity = request.Quantity
            });

            return result.Match(
                () => Results.NoContent(),
                error => ProblemForError(error));
        })
        .WithName("UpdateOrderItemQuantity")
        .RequireAuthorization("OrderOwnerOrAdmin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // PUT /api/v1/orders/{id}/shipping-address — Admin panel S8, endpoint #59.
        group.MapPut("/{id:guid}/shipping-address", async (
            Guid id, UpdateShippingAddressRequest request, IMediator mediator) =>
        {
            var result = await mediator.Send(new UpdateShippingAddressCommand
            {
                OrderId = id,
                Street = request.Street,
                City = request.City,
                State = request.State,
                ZipCode = request.ZipCode,
                Country = request.Country
            });

            return result.Match(
                () => Results.NoContent(),
                error => ProblemForError(error));
        })
        .WithName("UpdateShippingAddress")
        .RequireAuthorization("OrderOwnerOrAdmin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // POST /api/v1/orders/{id}/notes (admin only) — Admin panel S9, endpoint #60.
        // Admin, not OrderOwnerOrAdmin, and that is the security decision of this stage: a note is an
        // internal record an operator writes ABOUT a customer ("suspected chargeback", "refund approved
        // by finance"), so the subject of the note must not be able to read or write it.
        group.MapPost("/{id:guid}/notes", async (Guid id, AddOrderNoteRequest request, IMediator mediator) =>
        {
            var result = await mediator.Send(new AddOrderNoteCommand { OrderId = id, Body = request.Body });

            // Location is the owning order, like Catalog's product children (audit L25): a note is
            // reached only through its order's collection, and there is no per-note GET.
            return result.Match(
                value => Results.Created($"/api/v1/orders/{id}", new CreatedOrderNoteResponse(value)),
                error => ProblemForError(error));
        })
        .WithName("AddOrderNote")
        .RequireAuthorization("Admin")
        .Produces<CreatedOrderNoteResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // GET /api/v1/orders/{id}/notes (admin only) — Admin panel S9, endpoint #61.
        group.MapGet("/{id:guid}/notes", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetOrderNotesQuery { OrderId = id });

            return result.Match(
                value => Results.Ok(value),
                error => ProblemForError(error));
        })
        .WithName("GetOrderNotes")
        .RequireAuthorization("Admin")
        .Produces<IReadOnlyList<OrderNoteDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // GET /api/v1/orders/{id}/history (admin only) — Admin panel S9, endpoint #62.
        // Admin for the same reason as the notes: every row names the operator who caused the
        // transition, and a customer has no business learning staff identifiers.
        group.MapGet("/{id:guid}/history", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetOrderStatusHistoryQuery { OrderId = id });

            return result.Match(
                value => Results.Ok(value),
                error => ProblemForError(error));
        })
        .WithName("GetOrderStatusHistory")
        .RequireAuthorization("Admin")
        .Produces<IReadOnlyList<OrderStatusHistoryDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

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

        // POST /api/v1/orders/{id}/deliver (admin only) — audit L2, decision D13: nothing could make an
        // order Delivered before this.
        group.MapPost("/{id:guid}/deliver", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new DeliverOrderCommand { OrderId = id });

            return result.Match(
                () => Results.NoContent(),
                error => ProblemForError(error));
        })
        .WithName("DeliverOrder")
        .RequireAuthorization("Admin")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// One mapping from a handler's <see cref="Error"/> to a status, for every endpoint that addresses
    /// an order. Every code ending in <c>.NotFound</c> is a 404; a state conflict (the order is past the
    /// point where the request applies) is a 409; Catalog being unreachable is a 503, since the request
    /// may be perfectly valid; anything else — including <c>Validation.Failed</c> and
    /// <c>Order.ProductUnavailable</c> — is a 400. Before this, each endpoint hard-coded one status, so
    /// a missing order came back as 400 from cancel and the item endpoints, and a validation failure as
    /// 404 from GET.
    /// </summary>
    internal static IResult ProblemForError(Error error) => ProblemResults.For(error, StatusFor(error.Code));

    internal static int StatusFor(string errorCode) => errorCode switch
    {
        _ when errorCode.EndsWith(".NotFound", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
        "Order.NotPaidYet" or "Order.NotShippedYet" or "Order.NotModifiable" or "Order.NotCancellable"
            or "Order.AddressNotModifiable"
            => StatusCodes.Status409Conflict,
        "Catalog.Unavailable" => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status400BadRequest
    };
}

public record CancelOrderRequest(string Reason);

/// <summary>The body of PUT /api/v1/orders/{id}/items/{itemId}: the route already names both ids.</summary>
public record UpdateOrderItemQuantityRequest(int Quantity);

/// <summary>
/// The body of PUT /api/v1/orders/{id}/shipping-address — a whole address, because that is what an
/// <c>Address</c> is. Every field is required, as on create.
/// </summary>
public record UpdateShippingAddressRequest(
    string Street,
    string City,
    string State,
    string ZipCode,
    string Country);

/// <summary>The body of a 201 from POST /api/v1/orders — the same <c>{ "id": … }</c> the anonymous object gave.</summary>
public record CreateOrderResponse(Guid Id);

/// <summary>
/// The body of POST /api/v1/orders/{id}/notes (Admin panel S9). Only the text: the order comes from
/// the route and the author from the caller's own claims, never from the request.
/// </summary>
public record AddOrderNoteRequest(string Body);

/// <summary>The id of the note just written, so the client need not re-read the list to find it.</summary>
public record CreatedOrderNoteResponse(Guid Id);
