using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Events;
using EShop.BuildingBlocks.Messaging.Events;

namespace EShop.Basket.Application.Commands.CheckoutBasket;

/// <summary>
/// Builds the integration event Ordering turns into an order. It was the body of a MediatR notification handler that
/// enqueued through the fire-and-forget outbox; since Basket audit S3 the handler queues it inside checkout's atomic
/// commit instead, so the mapping is all that is left.
/// </summary>
public static class BasketCheckedOutEventMapper
{
    public static BasketCheckedOutEvent ToIntegrationEvent(BasketCheckedOutDomainEvent domainEvent, string? correlationId)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return new BasketCheckedOutEvent
        {
            // Basket audit D3. The integration event takes the domain event's id, so the checkoutId the client gets,
            // the outbox envelope's id, the MassTransit MessageId and the id Ordering logs and deduplicates on are one
            // Guid. It used to be a fresh one, and the checkoutId named nothing downstream (M7).
            EventId = domainEvent.EventId,
            OccurredOn = domainEvent.OccurredOn,
            UserId = domainEvent.UserId,
            Items = domainEvent.Items
                .Select(item => new CheckoutItem
                {
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    Price = item.Price,
                    Quantity = item.Quantity
                })
                .ToList(),
            TotalPrice = domainEvent.TotalPrice,
            // Ordering reads only the structured form, and rejects a message without it. The string stays populated
            // for anything that just displays the address.
            ShippingAddressDetails = new CheckoutShippingAddress
            {
                Street = domainEvent.ShippingAddress.Street,
                City = domainEvent.ShippingAddress.City,
                State = domainEvent.ShippingAddress.State,
                ZipCode = domainEvent.ShippingAddress.ZipCode,
                Country = domainEvent.ShippingAddress.Country
            },
            ShippingAddress = domainEvent.ShippingAddress.ToString(),
            // Debt 7 (S11): stated, not implied. Ordering refuses a checkout in any other currency.
            Currency = ShoppingBasket.Currency,
            CorrelationId = correlationId
        };
    }
}
