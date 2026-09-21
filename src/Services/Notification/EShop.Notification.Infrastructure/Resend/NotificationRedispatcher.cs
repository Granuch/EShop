using EShop.Notification.Domain.Interfaces;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Notification.Infrastructure.Resend;

/// <inheritdoc cref="INotificationRedispatcher"/>
/// <remarks>
/// Resolves <see cref="ISendEndpointProvider"/> lazily from the request scope rather than taking it in the constructor:
/// a host with no bus (Testing, or Development without RabbitMQ — <c>AddEShopBus</c> registers nothing then) must still
/// be able to build this and answer "no bus" with a 503, instead of failing to resolve the handler at all.
/// </remarks>
public sealed class NotificationRedispatcher : INotificationRedispatcher
{
    private readonly IServiceProvider _services;

    public NotificationRedispatcher(IServiceProvider services)
    {
        _services = services;
    }

    public bool IsAvailable => _services.GetService<ISendEndpointProvider>() is not null;

    public bool CanRedispatch(string eventType) => ResendableNotifications.TryGet(eventType, out _);

    public async Task RedispatchAsync(string eventType, string payload, CancellationToken cancellationToken = default)
    {
        if (!ResendableNotifications.TryGet(eventType, out var notification))
        {
            throw new InvalidOperationException($"No consumer delivers {eventType}; it cannot be resent.");
        }

        var sendEndpoints = _services.GetService<ISendEndpointProvider>()
            ?? throw new InvalidOperationException("This host runs no message bus.");

        var message = NotificationPayload.Deserialize(payload, notification.EventType);
        var endpoint = await sendEndpoints.GetSendEndpoint(new Uri($"queue:{notification.QueueName}"));

        // Sent with its declared type, so the consumer bound to NotificationConsumer<TEvent> receives it; the payload's
        // own EventId and CorrelationId travel with it, which is what lets the consumer find the existing row by EventId
        // and keeps the trace tied to the request that caused the original event.
        await endpoint.Send(message, notification.EventType, cancellationToken);
    }
}
