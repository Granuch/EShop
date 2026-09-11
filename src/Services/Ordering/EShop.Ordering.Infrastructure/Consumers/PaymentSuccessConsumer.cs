using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Infrastructure.Consumers;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;
using EShop.Ordering.Infrastructure.Caching;
using EShop.Ordering.Infrastructure.Data;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EShop.Ordering.Infrastructure.Consumers;

/// <summary>
/// Idempotent consumer for PaymentSuccessEvent.
/// Marks the order as paid, and also ships it only when
/// <see cref="PaymentSuccessProcessingOptions.AutoShipOnPaymentSuccess"/> is on (off by default).
/// </summary>
public class PaymentSuccessConsumer : IdempotentConsumer<PaymentSuccessEvent, OrderingDbContext>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly OrderCacheInvalidator _cacheInvalidator;
    private readonly PaymentSuccessProcessingOptions _processingOptions;

    /// <summary>Set once the order has been changed; read after commit to invalidate its caches.</summary>
    private (Guid OrderId, string UserId)? _changed;

    public PaymentSuccessConsumer(
        OrderingDbContext dbContext,
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        OrderCacheInvalidator cacheInvalidator,
        IOptions<PaymentSuccessProcessingOptions>? processingOptions,
        ILogger<PaymentSuccessConsumer> logger)
        : base(dbContext, logger)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _cacheInvalidator = cacheInvalidator;
        _processingOptions = processingOptions?.Value ?? new PaymentSuccessProcessingOptions();
    }

    protected override async Task HandleAsync(ConsumeContext<PaymentSuccessEvent> context, CancellationToken cancellationToken)
    {
        var message = context.Message;

        Logger.LogInformation(
            "Processing PaymentSuccessEvent for OrderId={OrderId}, PaymentIntentId={PaymentIntentId}",
            message.OrderId,
            message.PaymentIntentId);

        var order = await _orderRepository.GetByIdAsync(message.OrderId, cancellationToken);
        if (order is null)
        {
            Logger.LogWarning("Order {OrderId} not found for PaymentSuccessEvent", message.OrderId);
            return;
        }

        if (order.Status == OrderStatus.Paid)
        {
            Logger.LogInformation(
                "Order {OrderId} already marked as paid. Skipping duplicate PaymentSuccessEvent.",
                message.OrderId);
            return;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            // The customer was charged for an order that was cancelled while the payment was in flight.
            // Nothing refunds that automatically — there is no refund flow — so it needs a person, and a
            // retry would change nothing. Logged at Error so it is not lost among routine skips.
            Logger.LogError(
                "PaymentSuccessEvent for cancelled OrderId={OrderId}: PaymentIntentId={PaymentIntentId}, "
                + "Amount={Amount} was captured but the order is cancelled. Manual refund required.",
                message.OrderId,
                message.PaymentIntentId,
                message.Amount);
            return;
        }

        if (order.Status != OrderStatus.Pending)
        {
            Logger.LogWarning(
                "Skipping PaymentSuccessEvent for OrderId={OrderId} because order status is {Status}.",
                message.OrderId,
                order.Status);
            return;
        }

        // Throws DomainException when message.Amount differs from the order total. That is deliberate:
        // the message retries and then lands in the error queue with its payload intact for
        // reconciliation, rather than being acknowledged and forgotten.
        order.MarkAsPaid(message.PaymentIntentId, message.Amount);

        if (_processingOptions.AutoShipOnPaymentSuccess)
        {
            order.Ship();
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _changed = (order.Id, order.UserId);

        Logger.LogInformation(
            _processingOptions.AutoShipOnPaymentSuccess
                ? "Order {OrderId} marked as paid and shipped by payment-success orchestration"
                : "Order {OrderId} marked as paid by payment-success orchestration",
            message.OrderId);
    }

    /// <summary>
    /// After commit, so a concurrent read cannot re-cache the pre-payment state. This replaces a Redis
    /// SCAN over the whole keyspace on every payment (audit M8), which also left GET /orders/{id}
    /// showing "Pending" for up to five minutes, because it never evicted that key.
    /// </summary>
    protected override Task OnCommittedAsync(ConsumeContext<PaymentSuccessEvent> context, CancellationToken cancellationToken)
        => _changed is { } changed
            ? _cacheInvalidator.InvalidateAsync(changed.OrderId, changed.UserId, cancellationToken)
            : Task.CompletedTask;

    public sealed class PaymentSuccessProcessingOptions
    {
        public const string SectionName = "PaymentSuccessProcessing";

        /// <summary>
        /// Off by default: a paid order stays Paid until an admin ships it. When on, payment success
        /// ships the order in the same transaction, so the Paid state and POST /orders/{id}/ship are
        /// effectively skipped.
        /// </summary>
        public bool AutoShipOnPaymentSuccess { get; init; }
    }
}
