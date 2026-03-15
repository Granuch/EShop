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
