using System.Text.Json;
using EShop.BuildingBlocks.Messaging;

namespace EShop.Notification.Infrastructure.Resend;

/// <summary>
/// How an integration event is kept on its <c>NotificationLog</c> row and read back for a resend (Admin panel S13).
/// One class for both directions, so the two cannot disagree about the shape.
/// </summary>
public static class NotificationPayload
{
    /// <summary>
    /// System.Text.Json's general defaults — property names as declared. Not the web (camelCase) defaults: this JSON is
    /// read only by <see cref="Deserialize"/>, never by a client, and pinning the options here means a later change to
    /// some shared serializer setting cannot quietly make every stored payload unreadable.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General);

    /// <summary>
    /// The event as JSON, or <b>null for an <see cref="ISensitivePayloadEvent"/></b>. The password reset carries a live
    /// reset token; storing it would keep that token at rest for the log's 90-day retention, the exposure SEC-05's outbox
    /// redaction exists to prevent. A notification with no payload simply cannot be resent — for a reset link that is
    /// the right answer anyway: the customer requests a new one.
    /// </summary>
    public static string? Serialize<TEvent>(TEvent message)
        where TEvent : IntegrationEvent
        => message is ISensitivePayloadEvent
            ? null
            // typeof(TEvent), not message.GetType(): the declared event type is what the consumer receives, and it is
            // the type Deserialize is handed back.
            : JsonSerializer.Serialize(message, typeof(TEvent), Options);

    public static IntegrationEvent Deserialize(string payload, Type eventType)
        => JsonSerializer.Deserialize(payload, eventType, Options) as IntegrationEvent
           ?? throw new InvalidOperationException($"The stored payload is not a {eventType.Name}.");
}
