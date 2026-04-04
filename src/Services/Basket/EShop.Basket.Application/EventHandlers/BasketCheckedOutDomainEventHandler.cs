using EShop.Basket.Domain.Events;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Basket.Application.EventHandlers;

/// <summary>
/// Maps basket checkout domain events to integration events and enqueues them in the outbox.
/// </summary>
public class BasketCheckedOutDomainEventHandler : INotificationHandler<BasketCheckedOutDomainEvent>
{
    private readonly IIntegrationEventOutbox _outbox;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<BasketCheckedOutDomainEventHandler> _logger;

    public BasketCheckedOutDomainEventHandler(
        IIntegrationEventOutbox outbox,
        ICurrentUserContext currentUserContext,
        ILogger<BasketCheckedOutDomainEventHandler> logger)
    {
        _outbox = outbox;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    public Task Handle(BasketCheckedOutDomainEvent notification, CancellationToken cancellationToken)
    {
        var integrationEvent = new BasketCheckedOutEvent
        {
            UserId = notification.UserId,
            Items = notification.Items
                .Select(item => new CheckoutItem
                {
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    Price = item.Price,
                    Quantity = item.Quantity
                })
                .ToList(),
            TotalPrice = notification.TotalPrice,
            ShippingAddress = notification.ShippingAddress,
            PaymentMethod = notification.PaymentMethod,
            CorrelationId = _currentUserContext.CorrelationId
        };

        _outbox.Enqueue(integrationEvent, _currentUserContext.CorrelationId);

        _logger.LogInformation(
            "Basket checkout integration event enqueued. UserId={UserId}, EventId={EventId}",
            notification.UserId,
            integrationEvent.EventId);

        return Task.CompletedTask;
    }
}
