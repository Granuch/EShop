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
        var existing = await _paymentRepository.GetByOrderIdAsync(request.OrderId, cancellationToken);
        if (existing is not null)
        {
            return Result<CreatePaymentIntentDto>.Failure(new Error(
                "PAYMENT_ALREADY_EXISTS",
                "Payment already exists for this order."));
        }

        var payment = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = request.OrderId,
            UserId = request.UserId,
            Amount = request.Amount,
            Currency = (request.Currency ?? "USD").ToUpperInvariant(),
            PaymentMethod = "Stripe",
            Status = PaymentStatus.Processing,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            StripeStatus = "requires_payment_method"
        };

        await _paymentRepository.AddAsync(payment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            var stripeCustomerId = await _stripeCustomerService.CreateOrGetCustomerAsync(
                request.UserId,
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
            payment.Status = PaymentStatus.Failed;
            payment.ErrorMessage = ex.Message;
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
