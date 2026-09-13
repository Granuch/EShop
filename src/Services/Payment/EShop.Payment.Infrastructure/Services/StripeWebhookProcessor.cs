using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Application.Payments.Common;
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

        // Payment audit Stage 7 (H5). Which event may change which payment is the entity's rule; see the methods'
        // comments. Each returns false when the event changes nothing, and only a change is written.
        var now = DateTime.UtcNow;
        var changed = false;
        switch (stripeEvent.Type)
        {
            case "payment_intent.succeeded":
                changed = payment.RecordStripeSuccess(stripeEvent.Status, now);
                publishSuccess = changed;
                break;

            // Stage 5 (H1, D3): a decline keeps the payment open for another card, with no PaymentFailedEvent.
            case "payment_intent.payment_failed":
                changed = payment.RecordDeclinedAttempt(stripeEvent.FailureMessage, stripeEvent.Status, now);
                break;

            // Ordering audit Stage 21 (D17): tagged by OrderCancelledConsumer, it is a cancelled order, recorded
            // Cancelled with no PaymentFailedEvent. Untagged (Dashboard, Stripe), the payment failed.
            case "payment_intent.canceled":
                changed = payment.RecordStripeCancellation(stripeEvent.CancelRequestedByEShop, stripeEvent.Status, now);
                publishFailure = changed && payment.Status == PaymentStatus.Failed;
                break;
        }

        if (changed)
        {
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        await _paymentRepository.AddProcessedStripeEventAsync(processedEvent, cancellationToken);

        if (publishSuccess)
        {
            _integrationEventOutbox.EnqueuePaymentSucceeded(payment);
        }

        if (publishFailure)
        {
            _integrationEventOutbox.EnqueuePaymentFailed(payment);
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
