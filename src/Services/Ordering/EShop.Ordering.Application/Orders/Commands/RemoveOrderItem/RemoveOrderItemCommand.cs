using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Ordering.Application.Orders.Commands.RemoveOrderItem;

/// <summary>
/// Command to remove an item from an existing order
/// </summary>
public record RemoveOrderItemCommand : IRequest<Result>, ITransactionalCommand, ICacheInvalidatingCommand
{
    public Guid OrderId { get; init; }
    public Guid ItemId { get; init; }

    /// <summary>The user's list family is added by the handler, which is where the user id is known.</summary>
    public IEnumerable<string> CacheKeysToInvalidate => [OrderCacheKeys.Order(OrderId)];
}
