using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.Payment.Application.Payments.Common;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using MediatR;

namespace EShop.Payment.Application.Payments.Commands.SettleOfflinePayment;

public sealed class SettleOfflinePaymentCommandHandler
    : IRequestHandler<SettleOfflinePaymentCommand, Result<PaymentDto>>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IIntegrationEventOutbox _integrationEventOutbox;
    private readonly IUnitOfWork _unitOfWork;

    public SettleOfflinePaymentCommandHandler(
        IPaymentRepository paymentRepository,
        IIntegrationEventOutbox integrationEventOutbox,
        IUnitOfWork unitOfWork)
    {
        _paymentRepository = paymentRepository;
        _integrationEventOutbox = integrationEventOutbox;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PaymentDto>> Handle(
        SettleOfflinePaymentCommand request,
        CancellationToken cancellationToken)
    {
        // Every refusal below is a pre-check that runs before the aggregate is touched. TransactionBehavior commits on
        // any non-exception return, so a failure path that had already mutated the payment would answer 409 and
        // persist the change anyway.
        var payment = await _paymentRepository.GetByOrderIdAsync(request.OrderId, cancellationToken);
        if (payment is null)
        {
            return Result<PaymentDto>.Failure(new Error(
                "PAYMENT_NOT_FOUND",
                "No payment has been recorded for this order."));
        }

        if (payment.Status != PaymentStatus.Pending)
        {
            // The same code and wording as POST /api/v1/payments, because it is the same rule: a payment that is
            // already in flight, settled or closed is not one an operator may declare paid.
            return Result<PaymentDto>.Failure(new Error(
                "PAYMENT_NOT_PENDING",
                "Only a pending payment can be settled."));
        }

        // The unique filtered index on PaymentIntentId is the backstop for a lost race; this pre-check is what makes
        // the ordinary case say which order already holds the reference, instead of "a duplicate resource, retry" —
        // advice that can never succeed here. Same shape as Catalog's SKU and category-slug pre-checks.
        var storedReference = PaymentTransaction.OfflineReference(request.Reference);
        var existing = await _paymentRepository.GetByPaymentIntentIdAsync(storedReference, cancellationToken);
        if (existing is not null)
        {
            return Result<PaymentDto>.Failure(new Error(
                "PAYMENT_REFERENCE_IN_USE",
                $"Reference '{request.Reference.Trim()}' is already recorded against order {existing.OrderId}."));
        }

        payment.SettleOffline(request.Reference, DateTime.UtcNow);

        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        // PaymentSuccessEvent (Ordering) and PaymentCompletedEvent (Notification), from the record after its
        // transition. Deliberately not EnqueuePaymentStarted: PaymentCreatedEvent means "an attempt has started and
        // nothing is charged yet", which is false of money that has already arrived — and it would email the customer
        // "Payment started" and "Payment received" in the same second.
        _integrationEventOutbox.EnqueuePaymentSucceeded(payment);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<PaymentDto>.Success(payment.ToDto());
    }
}
