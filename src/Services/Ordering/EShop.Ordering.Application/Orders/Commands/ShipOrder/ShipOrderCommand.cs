using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Ordering.Application.Orders.Commands.ShipOrder;

/// <summary>
/// Command to ship an order (admin only)
/// </summary>
public record ShipOrderCommand : IRequest<Result>, ITransactionalCommand, ICacheInvalidatingCommand
{
    public Guid OrderId { get; init; }

    /// <summary>The user's list family is added by the handler, which is where the user id is known.</summary>
    public IEnumerable<string> CacheKeysToInvalidate => [OrderCacheKeys.Order(OrderId)];
}
