using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Ordering.Application.Orders.Commands.UpdateOrderItemQuantity;

/// <summary>
/// Changes how many of one line a pending order is for (Admin panel S8, endpoint #57).
///
/// <para>
/// Transactional, unlike <c>AddOrderItemCommand</c>: that one calls Catalog for a price and so must
/// not hold a connection across an external call, while this one touches nothing but the order — the
/// line's unit price is the snapshot taken when it was added and is deliberately not re-priced here.
/// </para>
/// </summary>
public record UpdateOrderItemQuantityCommand : IRequest<Result>, ITransactionalCommand, ICacheInvalidatingCommand
{
    public Guid OrderId { get; init; }
    public Guid ItemId { get; init; }
    public int Quantity { get; init; }

    /// <summary>The user's list family is added by the handler, which is where the user id is known.</summary>
    public IEnumerable<string> CacheKeysToInvalidate => [OrderCacheKeys.Order(OrderId)];
}
