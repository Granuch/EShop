using EShop.Payment.Domain.Entities;

namespace EShop.Payment.Application.Payments.Refunds;

/// <summary>
/// Ordering audit Stage 19. The one place a captured payment is refunded — used by the admin refund
/// endpoint and, when <c>CancelledOrders:AutoRefund</c> is on, by <c>OrderCancelledConsumer</c>, so the two
/// cannot drift apart in how they refund, record and announce it.
/// </summary>
public interface IPaymentRefunder
{
    /// <summary>
    /// Refunds <paramref name="payment"/> in full at its provider (refunds are full-only, D11), marks it
    /// <see cref="PaymentStatus.Refunded"/> and enqueues <c>PaymentRefundedEvent</c>. Does <b>not</b> save:
    /// the caller's unit of work does, so the record, the event and a consumer's message claim commit
    /// together. The caller decides whether the payment may be refunded.
    /// </summary>
    /// <exception cref="PaymentRefundRefusedException">
    /// The provider answered and refused. Nothing has been changed.
    /// </exception>
    /// <remarks>
    /// Any other failure (the provider unreachable) propagates unchanged, so a consumer retries it. A
    /// retry is safe: Stripe replays the first refund within its idempotency window and reports the
    /// charge as already refunded after it.
    /// </remarks>
    Task RefundInFullAsync(PaymentTransaction payment, CancellationToken cancellationToken = default);
}
