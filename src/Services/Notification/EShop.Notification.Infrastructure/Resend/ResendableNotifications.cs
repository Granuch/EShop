using EShop.BuildingBlocks.Infrastructure.Extensions;
using EShop.BuildingBlocks.Messaging;
using EShop.Notification.Infrastructure.Consumers;
using EShop.Notification.Infrastructure.Extensions;
using MassTransit;

namespace EShop.Notification.Infrastructure.Resend;

/// <summary>One event type an operator can resend, the consumer that delivers it, and the queue that consumer reads.</summary>
public sealed record ResendableNotification(Type EventType, Type ConsumerType, string QueueName);

/// <summary>
/// Every notification an operator can resend, keyed by <c>NotificationLog.EventType</c> (Admin panel S13).
///
/// <para>
/// <b>Discovered, not listed.</b> Every concrete <see cref="NotificationConsumer{TEvent}"/> in this assembly is included
/// unless its event is an <see cref="ISensitivePayloadEvent"/> — the same rule <see cref="NotificationPayload"/> uses to
/// decide what to keep, so a new consumer is resendable the day it is written and a hand-kept table cannot fall behind.
/// </para>
///
/// <para>
/// <b>The queue name comes from the formatter the bus itself is configured with</b>
/// (<see cref="ServiceCollectionExtensions.MessagingServiceName"/>, via
/// <see cref="MassTransitServiceCollectionExtensions.CreateEndpointNameFormatter"/>), so it is the queue
/// <c>ConfigureEndpoints</c> binds the consumer to: <c>notification_order_created</c>, never the bare
/// <c>order_created</c> that Payment's consumer of the same event also reads.
/// </para>
/// </summary>
public static class ResendableNotifications
{
    private static readonly IReadOnlyDictionary<string, ResendableNotification> ByEventType = Discover();

    public static IReadOnlyCollection<ResendableNotification> All => (IReadOnlyCollection<ResendableNotification>)ByEventType.Values;

    /// <param name="eventType">As stored in <c>NotificationLog.EventType</c>: the event's type name, e.g. <c>OrderCreatedEvent</c>.</param>
    public static bool TryGet(string eventType, out ResendableNotification notification)
        => ByEventType.TryGetValue(eventType, out notification!);

    private static Dictionary<string, ResendableNotification> Discover()
    {
        var formatter = MassTransitServiceCollectionExtensions.CreateEndpointNameFormatter(
            ServiceCollectionExtensions.MessagingServiceName);

        var consumerName = typeof(IEndpointNameFormatter).GetMethods()
            .Single(m => m.Name == nameof(IEndpointNameFormatter.Consumer)
                         && m.IsGenericMethodDefinition
                         && m.GetParameters().Length == 0);

        return typeof(ResendableNotifications).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && t.BaseType is { IsGenericType: true } baseType
                        && baseType.GetGenericTypeDefinition() == typeof(NotificationConsumer<>))
            .Select(consumer => (Consumer: consumer, Event: consumer.BaseType!.GetGenericArguments()[0]))
            .Where(x => !typeof(ISensitivePayloadEvent).IsAssignableFrom(x.Event))
            .ToDictionary(
                x => x.Event.Name,
                x => new ResendableNotification(
                    x.Event,
                    x.Consumer,
                    (string)consumerName.MakeGenericMethod(x.Consumer).Invoke(formatter, null)!),
                StringComparer.Ordinal);
    }
}
