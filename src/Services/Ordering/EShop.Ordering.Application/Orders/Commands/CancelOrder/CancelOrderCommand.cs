using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Ordering.Application.Orders.Commands.CancelOrder;

/// <summary>
/// Command to cancel an order
/// </summary>
public record CancelOrderCommand : IRequest<Result>, ITransactionalCommand, ICacheInvalidatingCommand
{
    public Guid OrderId { get; init; }
    public string Reason { get; init; } = string.Empty;

    /// <summary>The user's list family is added by the handler, which is where the user id is known.</summary>
    public IEnumerable<string> CacheKeysToInvalidate => [OrderCacheKeys.Order(OrderId)];
}
