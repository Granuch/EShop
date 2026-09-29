namespace EShop.Payment.Domain.Interfaces;

/// <summary>
/// Records a Stripe webhook delivery this service accepted and then failed to apply (Admin panel S11, endpoint #67).
///
/// <para>
/// <b>Implementations must write through their own scope</b>, never through the unit of work that just failed: after a
/// failed <c>SaveChanges</c> the DbContext still holds the rejected changes, and on Postgres its transaction is
/// aborted, so a capture written there is lost with the failure it exists to record. That is the plan's named risk for
/// this stage.
/// </para>
///
/// <para>
/// <b>And it must not throw.</b> The only caller is the webhook endpoint's last-resort <c>catch</c>, which owes Stripe
/// a 500 so it redelivers; a capture that threw would replace the original failure with a different one and lose it.
/// </para>
/// </summary>
public interface IFailedStripeWebhookStore
{
    /// <summary>
    /// Captures the delivery, or — when its Stripe event id has already been captured — records another failed attempt
    /// against the existing row.
    /// </summary>
    Task CaptureAsync(string payload, string signatureHeader, Exception failure, CancellationToken cancellationToken = default);
}
