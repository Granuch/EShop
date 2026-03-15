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
    public string UserId { get; set; } = string.Empty;

    public IEnumerable<string> CacheKeysToInvalidate =>
        string.IsNullOrWhiteSpace(UserId)
            ?
            [
                $"order:{OrderId}"
            ]
            :
            [
                $"order:{OrderId}",
                $"orders:user:{UserId}"
            ];
}
