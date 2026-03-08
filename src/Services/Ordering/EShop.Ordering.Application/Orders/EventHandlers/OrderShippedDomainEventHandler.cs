using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Ordering.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Ordering.Application.Orders.EventHandlers;

/// <summary>
/// Handles OrderShippedDomainEvent by enqueuing an OrderShippedEvent
/// for cross-service communication via the outbox pattern.
/// </summary>
public class OrderShippedDomainEventHandler : INotificationHandler<OrderShippedDomainEvent>
{
    private readonly IIntegrationEventOutbox _outbox;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<OrderShippedDomainEventHandler> _logger;

    public OrderShippedDomainEventHandler(
        IIntegrationEventOutbox outbox,
        ICurrentUserContext currentUserContext,
        ILogger<OrderShippedDomainEventHandler> logger)
    {
        _outbox = outbox;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    public Task Handle(OrderShippedDomainEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Order shipped: OrderId={OrderId}, UserId={UserId}",
            notification.OrderId,
            notification.UserId);

        _outbox.Enqueue(new OrderShippedEvent
        {
            OrderId = notification.OrderId,
            UserId = notification.UserId,
            ShippedAt = notification.OccurredOn,
            CorrelationId = _currentUserContext.CorrelationId
        }, _currentUserContext.CorrelationId);

        return Task.CompletedTask;
    }
}
