using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EShop.Payment.Infrastructure.Services;

public sealed class StripeWebhookProcessor : IStripeWebhookProcessor
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IStripePaymentService _stripePaymentService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IIntegrationEventOutbox _integrationEventOutbox;
    private readonly ILogger<StripeWebhookProcessor> _logger;

    public StripeWebhookProcessor(
        IPaymentRepository paymentRepository,
        IStripePaymentService stripePaymentService,
        IUnitOfWork unitOfWork,
        IIntegrationEventOutbox integrationEventOutbox,
        ILogger<StripeWebhookProcessor> logger)
    {
        _paymentRepository = paymentRepository;
        _stripePaymentService = stripePaymentService;
        _unitOfWork = unitOfWork;
        _integrationEventOutbox = integrationEventOutbox;
        _logger = logger;
    }

    public async Task<StripeWebhookProcessResult> ProcessAsync(string payload, string signatureHeader, CancellationToken cancellationToken = default)
    {
        var stripeEvent = _stripePaymentService.ConstructWebhookEvent(payload, signatureHeader);

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
            await SaveIdempotentAsync(stripeEvent.Id, cancellationToken);
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

            case "payment_intent.payment_failed":
                if (payment.Status != PaymentStatus.Refunded)
                {
                    payment.Status = PaymentStatus.Failed;
                    payment.StripeStatus = stripeEvent.Status;
                    payment.ErrorMessage = stripeEvent.FailureMessage ?? "Stripe payment failed.";
                    payment.ProcessedAt = DateTime.UtcNow;
                    payment.UpdatedAt = DateTime.UtcNow;
                    await _paymentRepository.UpdateAsync(payment, cancellationToken);
                    publishFailure = true;
                }
                break;

            case "payment_intent.canceled":
                if (payment.Status != PaymentStatus.Success && payment.Status != PaymentStatus.Refunded)
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
        await SaveIdempotentAsync(stripeEvent.Id, cancellationToken);

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

        return new StripeWebhookProcessResult(false, true, stripeEvent.Id, stripeEvent.Type);
    }

    private async Task SaveIdempotentAsync(string eventId, CancellationToken cancellationToken)
    {
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogInformation(ex, "Duplicate Stripe webhook event ignored: {EventId}", eventId);
        }
    }
}
