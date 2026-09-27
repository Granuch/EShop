using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Ordering.Application.Orders.EventHandlers;

/// <summary>
/// Handles OrderTotalChangedDomainEvent by enqueuing an OrderTotalChangedEvent for Payment, through the outbox
/// (frontend-contracts F-47).
///
/// <para>
/// <c>TotalAsOf</c> is the domain event's own <c>OccurredOn</c> — the instant the items changed — and not the
/// integration event's, which is stamped when this handler runs in the outbox processor, a poll or more later. Payment
/// orders competing totals by it, so it has to say when the total became true.
/// </para>
/// </summary>
public class OrderTotalChangedDomainEventHandler : INotificationHandler<OrderTotalChangedDomainEvent>
{
    private readonly IIntegrationEventOutbox _outbox;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<OrderTotalChangedDomainEventHandler> _logger;

    public OrderTotalChangedDomainEventHandler(
        IIntegrationEventOutbox outbox,
        ICurrentUserContext currentUserContext,
        ILogger<OrderTotalChangedDomainEventHandler> logger)
    {
        _outbox = outbox;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    public Task Handle(OrderTotalChangedDomainEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Order total changed: OrderId={OrderId}, UserId={UserId}, NewTotal={NewTotal}",
            notification.OrderId,
            notification.UserId,
            notification.NewTotal);

        _outbox.Enqueue(new OrderTotalChangedEvent
        {
            OrderId = notification.OrderId,
            UserId = notification.UserId,
            NewTotal = notification.NewTotal,
            Currency = Order.PricingCurrency,
            TotalAsOf = notification.OccurredOn,
            CorrelationId = _currentUserContext.CorrelationId
        }, _currentUserContext.CorrelationId);

        return Task.CompletedTask;
    }
}
