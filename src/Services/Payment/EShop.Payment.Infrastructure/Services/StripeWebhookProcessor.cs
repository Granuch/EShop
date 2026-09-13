using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EShop.Payment.Infrastructure.Services;

public sealed class StripeWebhookProcessor : IStripeWebhookProcessor
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IStripeWebhookEventParser _eventParser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IIntegrationEventOutbox _integrationEventOutbox;
    private readonly ILogger<StripeWebhookProcessor> _logger;

    public StripeWebhookProcessor(
        IPaymentRepository paymentRepository,
        IStripeWebhookEventParser eventParser,
        IUnitOfWork unitOfWork,
        IIntegrationEventOutbox integrationEventOutbox,
        ILogger<StripeWebhookProcessor> logger)
    {
        _paymentRepository = paymentRepository;
        _eventParser = eventParser;
        _unitOfWork = unitOfWork;
        _integrationEventOutbox = integrationEventOutbox;
        _logger = logger;
    }

    public async Task<StripeWebhookProcessResult> ProcessAsync(string payload, string signatureHeader, CancellationToken cancellationToken = default)
    {
        var stripeEvent = _eventParser.Parse(payload, signatureHeader);

        if (!stripeEvent.IsSupportedPaymentIntentEvent)
        {
            _logger.LogDebug(
                "Ignoring unsupported Stripe event type {EventType} for idempotent payment processing.",
                stripeEvent.Type);
            return new StripeWebhookProcessResult(false, false, stripeEvent.Id, stripeEvent.Type);
        }

        if (await _paymentRepository.IsStripeEventProcessedAsync(stripeEvent.Id, cancellationToken))
        {
            return new StripeWebhookProcessResult(true, true, stripeEvent.Id, stripeEvent.Type);
        }

        var payment = await _paymentRepository.GetByPaymentIntentIdAsync(stripeEvent.PaymentIntentId, cancellationToken);
        var processedEvent = new ProcessedStripeWebhookEvent
        {
            Id = Guid.NewGuid(),
            EventId = stripeEvent.Id,
            EventType = stripeEvent.Type,
            ProcessedAt = DateTime.UtcNow
        };

        if (payment is null)
        {
            await _paymentRepository.AddProcessedStripeEventAsync(processedEvent, cancellationToken);
            var savedNoPayment = await SaveIdempotentAsync(stripeEvent.Id, cancellationToken);
            if (!savedNoPayment)
            {
                return new StripeWebhookProcessResult(true, false, stripeEvent.Id, stripeEvent.Type);
            }
            _logger.LogWarning("Stripe webhook event {EventId} has no matching payment for intent {PaymentIntentId}", stripeEvent.Id, stripeEvent.PaymentIntentId);
            return new StripeWebhookProcessResult(false, false, stripeEvent.Id, stripeEvent.Type);
        }

        var publishSuccess = false;
        var publishFailure = false;
        var publishCompleted = false;

        switch (stripeEvent.Type)
        {
            case "payment_intent.succeeded":
                if (payment.Status != PaymentStatus.Success && payment.Status != PaymentStatus.Refunded)
                {
                    payment.Status = PaymentStatus.Success;
                    payment.StripeStatus = stripeEvent.Status;
                    payment.ErrorMessage = null;
                    payment.ProcessedAt = DateTime.UtcNow;
                    payment.UpdatedAt = DateTime.UtcNow;
                    await _paymentRepository.UpdateAsync(payment, cancellationToken);
                    publishSuccess = true;
                    publishCompleted = true;
                }
                break;

            // Payment audit Stage 5 (H1, D3). A decline ends one attempt, not the payment. Stripe returns the intent
            // to requires_payment_method, and the customer can pay it with another card. So the error is recorded
            // and the payment stays open, with no PaymentFailedEvent, which would have Ordering cancel the order.
            // This used to record Failed and publish that event: the order was cancelled while its intent stayed
            // payable. And because a decline can be delivered after a later success, it could also turn a paid
            // payment into Failed. Only a cancelled intent ends a Stripe payment (below).
            case "payment_intent.payment_failed":
                if (payment.Status is PaymentStatus.Pending or PaymentStatus.Processing)
                {
                    payment.StripeStatus = stripeEvent.Status;
                    payment.ErrorMessage = stripeEvent.FailureMessage ?? "Stripe payment attempt failed.";
                    payment.UpdatedAt = DateTime.UtcNow;
                    await _paymentRepository.UpdateAsync(payment, cancellationToken);
                }
                break;

            // Cancelled is left alone here: OrderCancelledConsumer cancelled the intent itself, and Stripe's own
            // payment_intent.canceled webhook follows. Recording that as Failed would overwrite why the payment
            // ended and publish a PaymentFailedEvent for an order that is already cancelled. (A succeeded webhook
            // is still recorded: if Stripe captured the money, the record must say so.)
            case "payment_intent.canceled":
                if (payment.Status != PaymentStatus.Success
                    && payment.Status != PaymentStatus.Refunded
                    && payment.Status != PaymentStatus.Cancelled
                    && stripeEvent.CancelRequestedByEShop)
                {
                    // Ordering audit Stage 21 (D17). OrderCancelledConsumer tagged this intent and cancelled
                    // it, and this webhook got here before that consumer committed. The order was cancelled;
                    // the payment did not fail. Record what the consumer will find, send no PaymentFailedEvent.
                    // The consumer then loses on the row version and its retry finds the payment Cancelled.
                    payment.Status = PaymentStatus.Cancelled;
                    payment.StripeStatus = stripeEvent.Status;
                    payment.ErrorMessage = "Payment intent cancelled because its order was cancelled.";
                    payment.ProcessedAt = DateTime.UtcNow;
                    payment.UpdatedAt = DateTime.UtcNow;
                    await _paymentRepository.UpdateAsync(payment, cancellationToken);
                }
                else if (payment.Status != PaymentStatus.Success
                    && payment.Status != PaymentStatus.Refunded
                    && payment.Status != PaymentStatus.Cancelled)
                {
                    payment.Status = PaymentStatus.Failed;
                    payment.StripeStatus = stripeEvent.Status;
                    payment.ErrorMessage = "Stripe payment intent canceled.";
                    payment.ProcessedAt = DateTime.UtcNow;
                    payment.UpdatedAt = DateTime.UtcNow;
                    await _paymentRepository.UpdateAsync(payment, cancellationToken);
                    publishFailure = true;
                }
                break;
        }

        await _paymentRepository.AddProcessedStripeEventAsync(processedEvent, cancellationToken);

        if (publishSuccess)
        {
            _integrationEventOutbox.Enqueue(new PaymentSuccessEvent
            {
                OrderId = payment.OrderId,
                PaymentIntentId = payment.PaymentIntentId,
                Amount = payment.Amount,
                ProcessedAt = payment.ProcessedAt ?? DateTime.UtcNow
            });
        }

        if (publishCompleted)
        {
            _integrationEventOutbox.Enqueue(new PaymentCompletedEvent
            {
                OrderId = payment.OrderId,
                UserId = payment.UserId,
                Amount = payment.Amount,
                Currency = payment.Currency,
                PaymentIntentId = payment.PaymentIntentId,
                CompletedAt = payment.ProcessedAt ?? DateTime.UtcNow
            });
        }

        if (publishFailure)
        {
            _integrationEventOutbox.Enqueue(new PaymentFailedEvent
            {
                OrderId = payment.OrderId,
                UserId = payment.UserId,
                Reason = payment.ErrorMessage ?? "Stripe payment failed.",
                FailedAt = payment.ProcessedAt ?? DateTime.UtcNow
            });
        }

        var saved = await SaveIdempotentAsync(stripeEvent.Id, cancellationToken);
        if (!saved)
        {
            return new StripeWebhookProcessResult(true, true, stripeEvent.Id, stripeEvent.Type);
        }

        return new StripeWebhookProcessResult(false, true, stripeEvent.Id, stripeEvent.Type);
    }

    /// <summary>
    /// Persists pending changes and returns <c>true</c> if the save succeeded,
    /// or <c>false</c> if it was rejected as a duplicate (unique-constraint violation on EventId).
    /// Any other database error is rethrown so it is not silently swallowed.
    /// </summary>
    private async Task<bool> SaveIdempotentAsync(string eventId, CancellationToken cancellationToken)
    {
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogInformation(ex, "Duplicate Stripe webhook event ignored: {EventId}", eventId);
            return false;
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    }
}
