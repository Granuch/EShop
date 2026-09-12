using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Infrastructure.Consumers;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Application.Payments.Refunds;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Configuration;
using EShop.Payment.Infrastructure.Data;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EShop.Payment.Infrastructure.Consumers;

/// <summary>
/// Ordering audit Stage 9. <b>A cancelled order must not be charged.</b> Until this consumer existed,
/// nothing in Payment consumed <c>OrderCancelledEvent</c>, so cancelling a Pending order left its
/// Stripe intent live: the customer could still pay, and Ordering could only log "manual refund
/// required" when the success arrived.
///
/// <para>The rule, by the payment's state when the cancellation arrives:</para>
/// <list type="bullet">
///   <item><b>No payment yet</b> — the cancellation overtook <c>OrderCreatedEvent</c> (separate queues,
///   no ordering between them). A <see cref="PaymentStatus.Cancelled"/> record is left behind:
///   <c>OrderCreatedConsumer</c> treats it as final and never charges, and the unique OrderId index
///   makes <c>CreatePaymentIntent</c> refuse too.</item>
///   <item><b>Pending or Processing</b> — a Stripe intent is cancelled at Stripe first, then the
///   payment is recorded Cancelled. With no intent recorded there is nothing at the provider to
///   cancel. A Stripe payment whose intent is still being created is not visible here at all:
///   <c>CreatePaymentIntentCommand</c> writes the row and the intent id in one transaction, so the two
///   writers meet on the unique OrderId index. Whichever inserts second fails — the HTTP request with
///   a 409, or this consumer with a retry that then finds the intent and cancels it.</item>
///   <item><b>Success</b>, or <b>Stripe refuses</b> because the intent already succeeded — the money
///   is taken. By default that is an error, thrown as <see cref="PaymentCancellationFailedException"/>,
///   so the message lands in the error queue for a refund rather than being acknowledged with a log
///   line. With <c>CancelledOrders:AutoRefund</c> on (Stage 19, D16) the payment is refunded in full here
///   instead — for a refused cancel, only once Stripe confirms the intent <c>succeeded</c>; one still
///   <c>processing</c> is thrown as before. A refund Stripe refuses is thrown too, so only the settled
///   cases skip the error queue.</item>
///   <item><b>Failed, Refunded, Cancelled</b> — nothing to do.</item>
/// </list>
/// <para>Any other failure (a Stripe outage, the database) propagates unchanged and is retried.</para>
/// </summary>
public class OrderCancelledConsumer : IdempotentConsumer<OrderCancelledEvent, PaymentDbContext>
{
    private const int MaxErrorMessageLength = 500;

    private static readonly HashSet<PaymentStatus> NothingToCancel =
    [
        PaymentStatus.Failed,
        PaymentStatus.Refunded,
        PaymentStatus.Cancelled
    ];

    /// <summary>The Stripe intent status that means the money was captured.</summary>
    private const string StripeIntentSucceeded = "succeeded";

    private readonly IPaymentRepository _paymentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IStripePaymentService _stripePaymentService;
    private readonly IPaymentRefunder _paymentRefunder;
    private readonly CancelledOrderRefundSettings _refundSettings;

    public OrderCancelledConsumer(
        PaymentDbContext dbContext,
        IPaymentRepository paymentRepository,
        IUnitOfWork unitOfWork,
        IStripePaymentService stripePaymentService,
        IPaymentRefunder paymentRefunder,
        IOptions<CancelledOrderRefundSettings> refundSettings,
        ILogger<OrderCancelledConsumer> logger)
        : base(dbContext, logger)
    {
        _paymentRepository = paymentRepository;
        _unitOfWork = unitOfWork;
        _stripePaymentService = stripePaymentService;
        _paymentRefunder = paymentRefunder;
        _refundSettings = refundSettings.Value;
    }

    protected override async Task HandleAsync(ConsumeContext<OrderCancelledEvent> context, CancellationToken cancellationToken)
    {
        var message = context.Message;
        var now = DateTime.UtcNow;

        var payment = await _paymentRepository.GetByOrderIdAsync(message.OrderId, cancellationToken);

        if (payment is null)
        {
            await _paymentRepository.AddAsync(new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                OrderId = message.OrderId,
                UserId = message.UserId,
                Amount = 0m,
                Currency = "USD",
                PaymentMethod = "None",
                Status = PaymentStatus.Cancelled,
                ErrorMessage = CancellationNote(message),
                CreatedAt = now,
                ProcessedAt = now,
                UpdatedAt = now
            }, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            Logger.LogInformation(
                "OrderId={OrderId} was cancelled before its payment started; recorded as cancelled so it is never charged.",
                message.OrderId);
            return;
        }

        if (NothingToCancel.Contains(payment.Status))
        {
            Logger.LogInformation(
                "Payment for cancelled OrderId={OrderId} is already {Status}; nothing to cancel.",
                message.OrderId,
                payment.Status);
            return;
        }

        if (payment.Status == PaymentStatus.Success)
        {
            if (!_refundSettings.AutoRefund)
            {
                throw new PaymentCancellationFailedException(
                    message.OrderId,
                    $"payment {payment.Id} ({payment.PaymentMethod}, intent '{payment.PaymentIntentId}') was already captured; "
                    + "it needs a refund, not a cancellation.");
            }

            await RefundCapturedPaymentAsync(payment, message, cancellationToken);
            return;
        }

        // Pending or Processing. Cancel at Stripe BEFORE touching the record: if Stripe refuses, the
        // record must still say what is true, and the exception rolls back the consumer's transaction.
        if (IsStripe(payment) && !string.IsNullOrEmpty(payment.PaymentIntentId))
        {
            try
            {
                var cancelled = await _stripePaymentService.CancelPaymentIntentAsync(payment.PaymentIntentId, cancellationToken);
                payment.StripeStatus = cancelled.Status;
            }
            catch (PaymentIntentNotCancellableException ex)
            {
                // The record still says Pending/Processing because the success webhook has not been
                // recorded yet. Refund only on Stripe's own word that the money was captured: an intent
                // still "processing" may yet fail, and refunding it is not possible.
                if (_refundSettings.AutoRefund
                    && await _stripePaymentService.GetPaymentIntentStatusAsync(payment.PaymentIntentId, cancellationToken)
                        == StripeIntentSucceeded)
                {
                    payment.StripeStatus = StripeIntentSucceeded;
                    await RefundCapturedPaymentAsync(payment, message, cancellationToken);
                    return;
                }

                throw new PaymentCancellationFailedException(
                    message.OrderId,
                    $"Stripe refused to cancel intent '{payment.PaymentIntentId}': {ex.StripeMessage}",
                    ex);
            }
        }

        payment.Status = PaymentStatus.Cancelled;
        payment.ErrorMessage = CancellationNote(message);
        payment.ProcessedAt = now;
        payment.UpdatedAt = now;

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        Logger.LogInformation(
            "Payment {PaymentId} for cancelled OrderId={OrderId} cancelled (intent '{PaymentIntentId}').",
            payment.Id,
            message.OrderId,
            payment.PaymentIntentId);
    }

    /// <summary>
    /// The automatic refund. A refusal is rethrown as <see cref="PaymentCancellationFailedException"/> so
    /// it dead-letters at once, like every other case a person must look at; a failure to reach the
    /// provider propagates unchanged and is retried. The refund, the record and the message claim commit
    /// together, so a redelivered cancellation finds the payment Refunded and does nothing.
    /// </summary>
    private async Task RefundCapturedPaymentAsync(
        PaymentTransaction payment,
        OrderCancelledEvent message,
        CancellationToken cancellationToken)
    {
        try
        {
            await _paymentRefunder.RefundInFullAsync(payment, cancellationToken);
        }
        catch (PaymentRefundRefusedException ex)
        {
            throw new PaymentCancellationFailedException(
                message.OrderId,
                $"payment {payment.Id} (intent '{payment.PaymentIntentId}') was captured and the automatic refund was refused: {ex.Reason}",
                ex);
        }

        payment.ErrorMessage = CancellationNote(message);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        Logger.LogWarning(
            "Payment {PaymentId} for cancelled OrderId={OrderId} had already been captured; refunded {Amount} {Currency} automatically (intent '{PaymentIntentId}').",
            payment.Id,
            message.OrderId,
            payment.Amount,
            payment.Currency,
            payment.PaymentIntentId);
    }

    private static bool IsStripe(PaymentTransaction payment)
        => string.Equals(payment.PaymentMethod, "Stripe", StringComparison.OrdinalIgnoreCase);

    private static string CancellationNote(OrderCancelledEvent message)
    {
        var note = $"Order cancelled: {message.Reason}";
        return note.Length <= MaxErrorMessageLength ? note : note[..MaxErrorMessageLength];
    }
}
