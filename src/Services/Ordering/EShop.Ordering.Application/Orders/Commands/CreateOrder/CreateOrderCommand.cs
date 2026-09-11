using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Ordering.Application.Orders.Commands.CreateOrder;

/// <summary>
/// Creates an order from POST /api/v1/orders. Items carry only a product and a quantity: the name
/// and price of every line come from Catalog (audit C1). Checkout uses
/// <c>CreateCheckedOutOrderCommand</c> instead, which is never bound from HTTP.
///
/// <para>
/// Deliberately not an <c>ITransactionalCommand</c>: the handler makes one Catalog call per item,
/// and TransactionBehavior would hold a database transaction open across all of them. The handler's
/// single <c>SaveChangesAsync</c> already writes the order and its outbox row atomically.
/// </para>
/// </summary>
public record CreateOrderCommand : IRequest<Result<Guid>>, ICacheInvalidatingCommand
{
    public string UserId { get; init; } = string.Empty;
    public List<CreateOrderItemDto> Items { get; init; } = new();
    public string Street { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string ZipCode { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;

    public IEnumerable<string> CacheKeysToInvalidate =>
    [
        $"orders:user:{UserId}"
    ];
}

/// <summary>
/// No name or price: a client that still sends them has them ignored, which is the point. Unknown
/// JSON properties are not an error on this endpoint.
/// </summary>
public record CreateOrderItemDto
{
    public Guid ProductId { get; init; }
    public int Quantity { get; init; }
}
