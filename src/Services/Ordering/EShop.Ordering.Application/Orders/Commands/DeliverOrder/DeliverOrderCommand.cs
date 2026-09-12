using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Ordering.Application.Orders.Commands.DeliverOrder;

/// <summary>
/// Marks a shipped order delivered (admin only). Ordering audit L2: <c>Order.Deliver()</c> existed but
/// nothing called it, so no order could ever become Delivered.
/// </summary>
public record DeliverOrderCommand : IRequest<Result>, ITransactionalCommand, ICacheInvalidatingCommand
{
    public Guid OrderId { get; init; }

    /// <summary>The user's list family is added by the handler, which is where the user id is known.</summary>
    public IEnumerable<string> CacheKeysToInvalidate => [OrderCacheKeys.Order(OrderId)];
}
