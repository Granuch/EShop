using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using MediatR;

namespace EShop.Payment.Application.Payments.Commands.CreatePaymentIntent;

public sealed class CreatePaymentIntentCommandHandler : IRequestHandler<CreatePaymentIntentCommand, Result<CreatePaymentIntentDto>>
{
    private const string StripeMethod = "Stripe";

    private readonly IPaymentRepository _paymentRepository;
    private readonly IStripeCustomerService _stripeCustomerService;
    private readonly IStripePaymentService _stripePaymentService;
    private readonly IIntegrationEventOutbox _integrationEventOutbox;
    private readonly IUnitOfWork _unitOfWork;

    public CreatePaymentIntentCommandHandler(
        IPaymentRepository paymentRepository,
        IStripeCustomerService stripeCustomerService,
        IStripePaymentService stripePaymentService,
        IIntegrationEventOutbox integrationEventOutbox,
        IUnitOfWork unitOfWork)
    {
        _paymentRepository = paymentRepository;
        _stripeCustomerService = stripeCustomerService;
        _stripePaymentService = stripePaymentService;
        _integrationEventOutbox = integrationEventOutbox;
        _unitOfWork = unitOfWork;
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
            || !string.Equals(payment.PaymentMethod, StripeMethod, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(payment.PaymentIntentId))
        {
            return Result<CreatePaymentIntentDto>.Failure(new Error(
                "PAYMENT_ALREADY_EXISTS",
                "Payment already exists for this order."));
        }

        // Payment audit Stage 6 (H2, M3). No catch here, on purpose. Every failure below used to be recorded as the
        // payment failing, with a PaymentFailedEvent, so a Stripe timeout cancelled the customer's order and the
        // payment could never be started again. Nothing about the payment is decided by a failure to reach Stripe.
        // The exception propagates, TransactionBehavior rolls back, and the customer retries: a temporary failure is
        // PaymentProviderUnavailableException (503), anything else a 500. The Stripe calls are idempotent (M1), so
        // the retry gets back the customer and intent a rolled-back attempt created.
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

        payment.StripeCustomerId = stripeCustomerId;
        payment.PaymentIntentId = stripeIntent.PaymentIntentId;
        payment.StripeStatus = stripeIntent.Status;
        payment.Status = PaymentStatus.Processing;
        payment.UpdatedAt = DateTime.UtcNow;

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        _integrationEventOutbox.Enqueue(new PaymentCreatedEvent
        {
            OrderId = payment.OrderId,
            UserId = payment.UserId,
            Amount = payment.Amount,
            Currency = payment.Currency,
            Status = payment.Status.ToString().ToUpperInvariant(),
            CreatedAt = payment.CreatedAt
        });

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<CreatePaymentIntentDto>.Success(new CreatePaymentIntentDto(
            payment.Id,
            stripeIntent.PaymentIntentId,
            stripeIntent.ClientSecret,
            stripeIntent.Status));
    }
}
