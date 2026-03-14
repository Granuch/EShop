using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Ordering.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Ordering.Application.Orders.EventHandlers;

/// <summary>
/// Handles OrderCancelledDomainEvent by enqueuing an OrderCancelledEvent
/// for cross-service communication via the outbox pattern.
/// </summary>
public class OrderCancelledDomainEventHandler : INotificationHandler<OrderCancelledDomainEvent>
{
    private readonly IIntegrationEventOutbox _outbox;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<OrderCancelledDomainEventHandler> _logger;

    public OrderCancelledDomainEventHandler(
        IIntegrationEventOutbox outbox,
        ICurrentUserContext currentUserContext,
        ILogger<OrderCancelledDomainEventHandler> logger)
    {
        _outbox = outbox;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    public Task Handle(OrderCancelledDomainEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Order cancelled: OrderId={OrderId}, UserId={UserId}, Reason={Reason}",
            notification.OrderId,
            notification.UserId,
            notification.Reason);

        _outbox.Enqueue(new OrderCancelledEvent
        {
            OrderId = notification.OrderId,
            UserId = notification.UserId,
            Reason = notification.Reason,
            CorrelationId = _currentUserContext.CorrelationId
        }, _currentUserContext.CorrelationId);

        return Task.CompletedTask;
    }
}
