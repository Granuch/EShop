using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Ordering.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Ordering.Application.Orders.EventHandlers;

/// <summary>
/// Handles OrderPaidDomainEvent by enqueuing an OrderPaidEvent
/// for cross-service communication via the outbox pattern.
/// </summary>
public class OrderPaidDomainEventHandler : INotificationHandler<OrderPaidDomainEvent>
{
    private readonly IIntegrationEventOutbox _outbox;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<OrderPaidDomainEventHandler> _logger;

    public OrderPaidDomainEventHandler(
        IIntegrationEventOutbox outbox,
        ICurrentUserContext currentUserContext,
        ILogger<OrderPaidDomainEventHandler> logger)
    {
        _outbox = outbox;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    public Task Handle(OrderPaidDomainEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Order paid: OrderId={OrderId}, PaymentIntentId={PaymentIntentId}",
            notification.OrderId,
            notification.PaymentIntentId);

        _outbox.Enqueue(new OrderPaidEvent
        {
            OrderId = notification.OrderId,
            UserId = notification.UserId,
            PaymentIntentId = notification.PaymentIntentId,
            CorrelationId = _currentUserContext.CorrelationId
        }, _currentUserContext.CorrelationId);

        return Task.CompletedTask;
    }
}
