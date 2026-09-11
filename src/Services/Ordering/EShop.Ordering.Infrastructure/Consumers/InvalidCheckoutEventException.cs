namespace EShop.Ordering.Infrastructure.Consumers;

/// <summary>
/// A <c>BasketCheckedOutEvent</c> that can never become an order — no structured address, or rejected
/// by validation or the order domain.
///
/// <para>
/// It derives from <see cref="ArgumentException"/> on purpose: the retry and delayed-redelivery
/// policies in <c>MassTransitServiceCollectionExtensions.AddMessaging</c> <c>Ignore&lt;ArgumentException&gt;</c>,
/// so the message goes straight to the <c>_error</c> queue with its payload intact, instead of being
/// retried for the better part of an hour to the same result. Change the base class only together
/// with those policies.
/// </para>
/// </summary>
public sealed class InvalidCheckoutEventException(string message, Exception? innerException = null)
    : ArgumentException(message, innerException);
