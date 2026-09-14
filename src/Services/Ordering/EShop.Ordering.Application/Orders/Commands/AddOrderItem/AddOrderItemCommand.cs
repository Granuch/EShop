using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Ordering.Application.Orders.Commands.AddOrderItem;

/// <summary>
/// Adds a product to a pending order. The line's name and price come from Catalog, not from the
/// request (audit C1) — a client that still sends them has them ignored.
///
/// <para>
/// Not an <c>ITransactionalCommand</c>, for the same reason as <c>CreateOrderCommand</c>: the handler
/// calls Catalog, and its single <c>SaveChangesAsync</c> is already atomic.
/// </para>
/// </summary>
public record AddOrderItemCommand : IRequest<Result>, ICacheInvalidatingCommand
{
    public Guid OrderId { get; init; }
    public Guid ProductId { get; init; }
    public int Quantity { get; init; }

    /// <summary>The user's list family is added by the handler, which is where the user id is known.</summary>
    public IEnumerable<string> CacheKeysToInvalidate => [OrderCacheKeys.Order(OrderId)];
}
