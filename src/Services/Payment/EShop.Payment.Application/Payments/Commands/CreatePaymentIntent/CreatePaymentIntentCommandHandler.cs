using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Payment.Application.Payments.Commands.CreatePaymentIntent;

public sealed class CreatePaymentIntentCommandHandler : IRequestHandler<CreatePaymentIntentCommand, Result<CreatePaymentIntentDto>>
{
    private const string StripeMethod = "Stripe";

    private readonly IPaymentRepository _paymentRepository;
    private readonly IStripeCustomerService _stripeCustomerService;
    private readonly IStripePaymentService _stripePaymentService;
    private readonly IIntegrationEventOutbox _integrationEventOutbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CreatePaymentIntentCommandHandler> _logger;

    public CreatePaymentIntentCommandHandler(
        IPaymentRepository paymentRepository,
        IStripeCustomerService stripeCustomerService,
        IStripePaymentService stripePaymentService,
        IIntegrationEventOutbox integrationEventOutbox,
        IUnitOfWork unitOfWork,
        ILogger<CreatePaymentIntentCommandHandler> logger)
    {
        _paymentRepository = paymentRepository;
        _stripeCustomerService = stripeCustomerService;
        _stripePaymentService = stripePaymentService;
        _integrationEventOutbox = integrationEventOutbox;
        _unitOfWork = unitOfWork;
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
            || !string.Equals(payment.PaymentMethod, StripeMethod, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(payment.PaymentIntentId))
        {
            return Result<CreatePaymentIntentDto>.Failure(new Error(
                "PAYMENT_ALREADY_EXISTS",
                "Payment already exists for this order."));
        }

        try
        {
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Stripe payment intent for order {OrderId} and user {UserId}", payment.OrderId, payment.UserId);

            payment.Status = PaymentStatus.Failed;
            payment.ErrorMessage = "Failed to create Stripe payment intent.";
            payment.StripeStatus = "failed";
            payment.ProcessedAt = DateTime.UtcNow;
            payment.UpdatedAt = DateTime.UtcNow;

            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _integrationEventOutbox.Enqueue(new PaymentFailedEvent
            {
                OrderId = payment.OrderId,
                UserId = payment.UserId,
                Reason = payment.ErrorMessage,
                FailedAt = payment.ProcessedAt ?? DateTime.UtcNow
            });

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<CreatePaymentIntentDto>.Failure(new Error(
                "STRIPE_PAYMENT_INTENT_FAILED",
                "Failed to create Stripe payment intent in sandbox mode."));
        }
    }
}
