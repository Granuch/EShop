using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Application.Payments.Common;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Payment.Application.Payments.Commands.CreatePaymentIntent;

public sealed class CreatePaymentIntentCommandHandler : IRequestHandler<CreatePaymentIntentCommand, Result<CreatePaymentIntentDto>>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IStripeCustomerService _stripeCustomerService;
    private readonly IStripePaymentService _stripePaymentService;
    private readonly IIntegrationEventOutbox _integrationEventOutbox;
    private readonly ILogger<CreatePaymentIntentCommandHandler> _logger;

    public CreatePaymentIntentCommandHandler(
        IPaymentRepository paymentRepository,
        IStripeCustomerService stripeCustomerService,
        IStripePaymentService stripePaymentService,
        IIntegrationEventOutbox integrationEventOutbox,
        ILogger<CreatePaymentIntentCommandHandler> logger)
    {
        _paymentRepository = paymentRepository;
        _stripeCustomerService = stripeCustomerService;
        _stripePaymentService = stripePaymentService;
        _integrationEventOutbox = integrationEventOutbox;
        _logger = logger;
    }

    public async Task<Result<CreatePaymentIntentDto>> Handle(CreatePaymentIntentCommand request, CancellationToken cancellationToken)
    {
        // Payment audit Stage 2 (C2, D4). The payment to start is the one OrderCreatedConsumer recorded for the
        // order (D1), and Stripe is asked for exactly its amount and currency. The client used to send both, so a
        // customer could pay 100 JPY for a $100 order: PaymentSuccessEvent carries no currency, and Ordering's
        // MarkAsPaid compares only the number.
        var payment = await _paymentRepository.GetByOrderIdAsync(request.OrderId, cancellationToken);
        if (payment is null)
        {
            return Result<CreatePaymentIntentDto>.Failure(new Error(
                "PAYMENT_NOT_READY",
                "The order's payment is not ready yet. Retry shortly."));
        }

        // Not 403: a customer must not learn that someone else's order exists.
        if (!request.RequesterIsAdmin
            && !string.Equals(payment.UserId, request.RequesterId, StringComparison.OrdinalIgnoreCase))
        {
            return Result<CreatePaymentIntentDto>.Failure(new Error(
                "PAYMENT_NOT_FOUND",
                "Payment not found."));
        }

        if (payment.Status != PaymentStatus.Pending
            || payment.PaymentMethod != PaymentMethodType.Stripe
            || !string.IsNullOrEmpty(payment.PaymentIntentId))
        {
            return AlreadyExists();
        }

        // Payment audit D7. No transaction is open here (the command is not an ITransactionalCommand), so no pooled
        // connection is held while Stripe is called. Inside one, each checkout held a connection across two Stripe
        // round-trips, each up to 80 s and retried twice by Stripe.net. So a Stripe incident could drain Payment's pool
        // and take the webhook and the consumers down with it.
        //
        // Stage 6 (H2, M3). No catch either. Failing to reach Stripe says nothing about the payment. It used to be
        // recorded Failed with a PaymentFailedEvent, which cancelled the order. Nothing has been written, so the
        // exception just propagates: a temporary failure is PaymentProviderUnavailableException (503), anything else
        // a 500. The calls are idempotent (M1), so the retry gets back the same customer and intent.
        var stripeCustomerId = await _stripeCustomerService.CreateOrGetCustomerAsync(
            payment.UserId,
            request.Email,
            cancellationToken);

        var stripeIntent = await _stripePaymentService.CreatePaymentIntentAsync(new StripePaymentIntentRequest(
            payment.Id,
            payment.OrderId,
            payment.UserId,
            stripeCustomerId,
            payment.Amount,
            payment.Currency), cancellationToken);

        payment.StartStripePayment(stripeIntent.PaymentIntentId, stripeCustomerId, stripeIntent.Status, DateTime.UtcNow);

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        _integrationEventOutbox.EnqueuePaymentStarted(payment);

        // One save, so the intent and PaymentCreatedEvent are recorded together, guarded by the payment's row version.
        if (await _paymentRepository.TrySaveChangesAsync(cancellationToken))
        {
            return Started(payment.Id, stripeIntent);
        }

        return await AfterLosingTheSaveAsync(request.OrderId, stripeIntent, cancellationToken);
    }

    /// <summary>Another writer changed the payment while Stripe was being called.</summary>
    private async Task<Result<CreatePaymentIntentDto>> AfterLosingTheSaveAsync(
        Guid orderId,
        StripePaymentIntentResult stripeIntent,
        CancellationToken cancellationToken)
    {
        var current = await _paymentRepository.GetCurrentByOrderIdAsync(orderId, cancellationToken);

        // A second request for the same order was sent the same intent (same idempotency key) and recorded it first.
        if (current is not null && current.PaymentIntentId == stripeIntent.PaymentIntentId)
        {
            return Started(current.Id, stripeIntent);
        }

        // The order was cancelled, or the payment was settled another way, in the meantime. This intent is recorded
        // nowhere and its client secret goes to nobody, so it is cancelled here rather than left open at Stripe.
        // OrderCancelledConsumer cannot do it, because it never saw the intent.
        try
        {
            await _stripePaymentService.CancelPaymentIntentAsync(stripeIntent.PaymentIntentId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Could not cancel Stripe intent {PaymentIntentId} for order {OrderId}, which changed while it was being created. The intent stays open at Stripe, and nobody holds its client secret.",
                stripeIntent.PaymentIntentId,
                orderId);
        }

        return AlreadyExists();
    }

    private static Result<CreatePaymentIntentDto> Started(Guid paymentId, StripePaymentIntentResult stripeIntent)
        => Result<CreatePaymentIntentDto>.Success(new CreatePaymentIntentDto(
            paymentId,
            stripeIntent.PaymentIntentId,
            stripeIntent.ClientSecret,
            stripeIntent.Status));

    private static Result<CreatePaymentIntentDto> AlreadyExists()
        => Result<CreatePaymentIntentDto>.Failure(new Error(
            "PAYMENT_ALREADY_EXISTS",
            "Payment already exists for this order."));
}
