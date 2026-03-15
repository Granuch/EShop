using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Ordering.Application.Orders.Commands.AddOrderItem;

/// <summary>
/// Command to add an item to an existing order
/// </summary>
public record AddOrderItemCommand : IRequest<Result>, ITransactionalCommand, ICacheInvalidatingCommand
{
    public Guid OrderId { get; init; }
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
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
