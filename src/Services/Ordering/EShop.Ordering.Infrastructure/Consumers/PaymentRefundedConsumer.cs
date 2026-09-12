using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Infrastructure.Consumers;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;
using EShop.Ordering.Infrastructure.Caching;
using EShop.Ordering.Infrastructure.Data;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace EShop.Ordering.Infrastructure.Consumers;

/// <summary>
/// Ordering audit Stage 11 (L2). Payment publishes <c>PaymentRefundedEvent</c> when an admin refunds a
/// payment, always in full. Nothing in Ordering consumed it, so a refunded order went on saying Paid or
/// Shipped and <see cref="OrderStatus.Refunded"/> was never set. <see cref="Order.Refund"/> decides which
/// states move; a cancelled order stays Cancelled, and a repeat of the event is skipped.
/// </summary>
public class PaymentRefundedConsumer : IdempotentConsumer<PaymentRefundedEvent, OrderingDbContext>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly OrderCacheInvalidator _cacheInvalidator;

    /// <summary>Set once the order has been changed; read after commit to invalidate its caches.</summary>
    private (Guid OrderId, string UserId)? _changed;

    public PaymentRefundedConsumer(
        OrderingDbContext dbContext,
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        OrderCacheInvalidator cacheInvalidator,
        ILogger<PaymentRefundedConsumer> logger)
        : base(dbContext, logger)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _cacheInvalidator = cacheInvalidator;
    }

    protected override async Task HandleAsync(ConsumeContext<PaymentRefundedEvent> context, CancellationToken cancellationToken)
    {
        var message = context.Message;

        var order = await _orderRepository.GetByIdAsync(message.OrderId, cancellationToken);
        if (order is null)
        {
            Logger.LogWarning("Order {OrderId} not found for PaymentRefundedEvent", message.OrderId);
            return;
        }

        if (order.Status == OrderStatus.Refunded)
        {
            Logger.LogInformation(
                "Order {OrderId} is already refunded. Skipping duplicate PaymentRefundedEvent.",
                message.OrderId);
            return;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            // Money captured for an order that was cancelled while its payment was in flight, now returned.
            // The order is already final; the refund only settles the payment side.
            Logger.LogInformation(
                "Refund of {Amount} for cancelled OrderId={OrderId} settles its payment; the order stays cancelled.",
                message.Amount,
                message.OrderId);
            return;
        }

        order.Refund();

        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _changed = (order.Id, order.UserId);

        Logger.LogInformation(
            "Order {OrderId} marked as refunded (PaymentIntentId={PaymentIntentId}, Amount={Amount}).",
            message.OrderId,
            message.PaymentIntentId,
            message.Amount);
    }

    /// <summary>After commit, so a concurrent read cannot re-cache the pre-refund state.</summary>
    protected override Task OnCommittedAsync(ConsumeContext<PaymentRefundedEvent> context, CancellationToken cancellationToken)
        => _changed is { } changed
            ? _cacheInvalidator.InvalidateAsync(changed.OrderId, changed.UserId, cancellationToken)
            : Task.CompletedTask;
}
