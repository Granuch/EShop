namespace EShop.Notification.Domain.Interfaces;

/// <summary>
/// Hands a notification's stored event back to the delivery path (Admin panel S13, endpoints #72/#73).
///
/// <para>
/// <b>It redelivers; it does not deliver.</b> The event is sent to this service's <i>own</i> consumer queue, so the
/// resend runs through exactly the flow the first delivery did — the <c>NotificationLogs</c> claim, the attempt lease,
/// the row version, the retry policy and the error queue — rather than a second, hand-written send path that would
/// have to re-implement all of them. A resend that races a redelivery already in flight is therefore safe by
/// construction: one claims the row, the other finds it Sending or Sent.
/// </para>
///
/// <para>
/// <b>Sent, never published.</b> Publishing the event again would reach every service subscribed to it — Payment would
/// open a second payment for a re-published <c>OrderCreatedEvent</c>. Only Notification's queue receives it.
/// </para>
///
/// <para>In Domain because Application calls it and Infrastructure owns the bus — the repo's rule for a cross-layer call.</para>
/// </summary>
public interface INotificationRedispatcher
{
    /// <summary>False when this host runs no message bus: Testing, or Development with no RabbitMQ configured.</summary>
    bool IsAvailable { get; }

    /// <summary>Whether a consumer exists that can deliver an event of this stored type again.</summary>
    bool CanRedispatch(string eventType);

    /// <summary>Sends the stored event to the queue of the consumer that delivers it. Throws when the send fails.</summary>
    Task RedispatchAsync(string eventType, string payload, CancellationToken cancellationToken = default);
}
