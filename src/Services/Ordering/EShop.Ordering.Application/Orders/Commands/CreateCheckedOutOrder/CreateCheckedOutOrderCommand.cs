using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;

namespace EShop.Ordering.Application.Orders.Commands.CreateCheckedOutOrder;

/// <summary>
/// Creates the order for a basket checkout. <b>Internal only</b>: sent by
/// <c>BasketCheckedOutConsumer</c>, never bound from an HTTP request.
///
/// <para>
/// Unlike <c>CreateOrderCommand</c> it carries prices, because these were not supplied by a client:
/// Basket priced every line from Catalog on the server when the item was added, and reprices it on
/// <c>ProductPriceChangedIntegrationEvent</c>. They are the prices the customer saw at checkout, so
/// repricing here would charge a figure the customer never agreed to.
/// </para>
///
/// <para>
/// Deliberately neither an <c>ITransactionalCommand</c> nor an <c>ICacheInvalidatingCommand</c>. The
/// consumer's <c>IdempotentConsumer</c> transaction wraps it, so TransactionBehavior would do nothing
/// — and CacheInvalidationBehavior would invalidate <i>before</i> that transaction commits, letting a
/// concurrent read re-cache the list without the new order. The consumer invalidates after commit.
/// </para>
/// </summary>
public record CreateCheckedOutOrderCommand : IRequest<Result<Guid>>
{
    public string UserId { get; init; } = string.Empty;
    public List<CheckedOutOrderItem> Items { get; init; } = new();
    // A home address is personal data; LoggingBehavior logs every command at Information (audit L7).
    [SensitiveData] public string Street { get; init; } = string.Empty;
    [SensitiveData] public string City { get; init; } = string.Empty;
    [SensitiveData] public string State { get; init; } = string.Empty;
    [SensitiveData] public string ZipCode { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
}

public record CheckedOutOrderItem
{
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
}
