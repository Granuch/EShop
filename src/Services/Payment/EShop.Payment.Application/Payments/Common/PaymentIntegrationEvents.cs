using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Domain.Entities;

namespace EShop.Payment.Application.Payments.Common;

/// <summary>
/// Payment audit Stage 8 (M5). This is the one place that decides what each payment event announces, and where its
/// fields come from. They all come from the payment record, after its transition.
/// <para>Four writers used to build these events by hand, and they disagreed:</para>
/// <list type="bullet">
///   <item>the simulator's success carried the incoming order's total and the clock, rather than the record's amount
///   and <see cref="PaymentTransaction.ProcessedAt"/>;</item>
///   <item><c>PaymentSuccessEvent</c> and <c>PaymentCompletedEvent</c>, which state one fact, were built separately
///   each time.</item>
/// </list>
/// <para>Each method refuses a payment whose status is not the one its event announces. So a writer that forgets the
/// transition cannot announce a success the record does not show.</para>
/// </summary>
public static class PaymentIntegrationEvents
{
    /// <summary>
    /// <c>PaymentCreatedEvent</c>: an attempt has started (<see cref="PaymentStatus.Processing"/>), and nothing has been
    /// charged yet. Send it once per payment, and not again when a payment already in flight is resumed.
    /// </summary>
    public static void EnqueuePaymentStarted(
        this IIntegrationEventOutbox outbox,
        PaymentTransaction payment,
        string? correlationId = null)
    {
        RequireStatus(payment, PaymentStatus.Processing, nameof(PaymentCreatedEvent));

        outbox.Enqueue(new PaymentCreatedEvent
        {
            CorrelationId = correlationId,
            OrderId = payment.OrderId,
            UserId = payment.UserId,
            Amount = payment.Amount,
            Currency = payment.Currency,
            Status = payment.Status.ToString().ToUpperInvariant(),
            CreatedAt = payment.CreatedAt
        }, correlationId);
    }

    /// <summary>
    /// The payment was captured: <c>PaymentSuccessEvent</c> for Ordering and <c>PaymentCompletedEvent</c> for
    /// Notification, built from one snapshot of the record.
    /// </summary>
    public static void EnqueuePaymentSucceeded(
        this IIntegrationEventOutbox outbox,
        PaymentTransaction payment,
        string? correlationId = null)
    {
        var at = RequireSettled(payment, PaymentStatus.Success, nameof(PaymentSuccessEvent));

        outbox.Enqueue(new PaymentSuccessEvent
        {
            CorrelationId = correlationId,
            OrderId = payment.OrderId,
            PaymentIntentId = payment.PaymentIntentId,
            Amount = payment.Amount,
            Currency = payment.Currency,
            ProcessedAt = at
        }, correlationId);

        outbox.Enqueue(new PaymentCompletedEvent
        {
            CorrelationId = correlationId,
            OrderId = payment.OrderId,
            UserId = payment.UserId,
            Amount = payment.Amount,
            Currency = payment.Currency,
            PaymentIntentId = payment.PaymentIntentId,
            CompletedAt = at
        }, correlationId);
    }

    /// <summary><c>PaymentFailedEvent</c>: the payment has ended without being captured, and Ordering cancels the order.</summary>
    public static void EnqueuePaymentFailed(
        this IIntegrationEventOutbox outbox,
        PaymentTransaction payment,
        string? correlationId = null)
    {
        var at = RequireSettled(payment, PaymentStatus.Failed, nameof(PaymentFailedEvent));

        outbox.Enqueue(new PaymentFailedEvent
        {
            CorrelationId = correlationId,
            OrderId = payment.OrderId,
            UserId = payment.UserId,
            Reason = payment.ErrorMessage ?? "Payment failed.",
            FailedAt = at
        }, correlationId);
    }

    /// <summary><c>PaymentRefundedEvent</c>: the money went back to the customer.</summary>
    public static void EnqueuePaymentRefunded(
        this IIntegrationEventOutbox outbox,
        PaymentTransaction payment,
        string? correlationId = null)
    {
        var at = RequireSettled(payment, PaymentStatus.Refunded, nameof(PaymentRefundedEvent));

        outbox.Enqueue(new PaymentRefundedEvent
        {
            CorrelationId = correlationId,
            OrderId = payment.OrderId,
            UserId = payment.UserId,
            PaymentIntentId = payment.PaymentIntentId,
            Amount = payment.Amount,
            Currency = payment.Currency,
            RefundedAt = at
        }, correlationId);
    }

    private static void RequireStatus(PaymentTransaction payment, PaymentStatus status, string eventName)
    {
        if (payment.Status != status)
        {
            throw new InvalidOperationException(
                $"Payment {payment.Id} is {payment.Status}; {eventName} announces a {status} payment.");
        }
    }

    private static DateTime RequireSettled(PaymentTransaction payment, PaymentStatus status, string eventName)
    {
        RequireStatus(payment, status, eventName);
        return payment.ProcessedAt
            ?? throw new InvalidOperationException($"Payment {payment.Id} is {status} but has no ProcessedAt.");
    }
}
