using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Ordering.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Ordering.Application.Orders.EventHandlers;

/// <summary>
/// Handles OrderCreatedDomainEvent by enqueuing an OrderCreatedEvent
/// for cross-service communication via the outbox pattern.
/// </summary>
public class OrderCreatedDomainEventHandler : INotificationHandler<OrderCreatedDomainEvent>
{
    private readonly IIntegrationEventOutbox _outbox;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<OrderCreatedDomainEventHandler> _logger;

    public OrderCreatedDomainEventHandler(
        IIntegrationEventOutbox outbox,
        ICurrentUserContext currentUserContext,
        ILogger<OrderCreatedDomainEventHandler> logger)
    {
        _outbox = outbox;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    public Task Handle(OrderCreatedDomainEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Order created: OrderId={OrderId}, UserId={UserId}, TotalAmount={TotalAmount}",
            notification.OrderId,
            notification.UserId,
            notification.TotalAmount);

        _outbox.Enqueue(new OrderCreatedEvent
        {
            OrderId = notification.OrderId,
            UserId = notification.UserId,
            TotalAmount = notification.TotalAmount,
            CorrelationId = _currentUserContext.CorrelationId
        }, _currentUserContext.CorrelationId);

        return Task.CompletedTask;
    }
}
