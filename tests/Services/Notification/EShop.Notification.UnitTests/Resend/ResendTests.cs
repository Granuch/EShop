using EShop.BuildingBlocks.Infrastructure.Extensions;
using EShop.BuildingBlocks.Messaging;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Notification.Infrastructure.Consumers;
using EShop.Notification.Infrastructure.Extensions;
using EShop.Notification.Infrastructure.Resend;

namespace EShop.Notification.UnitTests.Resend;

/// <summary>
/// Admin panel S13. What a notification keeps of its event, and where a resend of it is sent. The end-to-end half —
/// that a resend sent to that queue is actually delivered by the consumer — is the integration suite's
/// <c>Journal/NotificationActionsTests</c>.
/// </summary>
[TestFixture]
public class ResendTests
{
    [Test]
    public void AnEvent_RoundTripsThroughItsStoredPayload_WithItsIdentityIntact()
    {
        var order = new OrderCreatedEvent
        {
            EventId = Guid.NewGuid(),
            CorrelationId = "corr-123",
            OccurredOn = new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Utc),
            OrderId = Guid.NewGuid(),
            UserId = "user-1",
            TotalAmount = 99.95m,
            Currency = "EUR",
            Items = [new OrderEventItem { ProductId = Guid.NewGuid(), Quantity = 3 }]
        };

        var back = (OrderCreatedEvent)NotificationPayload.Deserialize(
            NotificationPayload.Serialize(order)!, typeof(OrderCreatedEvent));

        Assert.Multiple(() =>
        {
            // EventId is what lets the consumer find the existing row instead of starting a second notification;
            // CorrelationId is what keeps the resend on the original request's trace.
            Assert.That(back.EventId, Is.EqualTo(order.EventId));
            Assert.That(back.CorrelationId, Is.EqualTo("corr-123"));
            Assert.That(back.OccurredOn, Is.EqualTo(order.OccurredOn));
            Assert.That(back.TotalAmount, Is.EqualTo(99.95m));
            Assert.That(back.Currency, Is.EqualTo("EUR"));
            Assert.That(back.Items.Single().Quantity, Is.EqualTo(3), "the email counts units from the item list");
        });
    }

    [Test]
    public void APasswordReset_KeepsNoPayload_BecauseItsTokenIsLive()
    {
        var reset = new PasswordResetRequestedIntegrationEvent { UserId = "user-1", ResetToken = "live-reset-token" };

        Assert.Multiple(() =>
        {
            Assert.That(reset, Is.InstanceOf<ISensitivePayloadEvent>(), "precondition: the rule keys on this marker");
            Assert.That(NotificationPayload.Serialize(reset), Is.Null);
        });
    }

    [Test]
    public void EveryConsumerButThePasswordReset_IsResendable()
    {
        Assert.That(ResendableNotifications.All.Select(n => n.EventType), Is.EquivalentTo(new[]
        {
            typeof(OrderCreatedEvent),
            typeof(OrderShippedEvent),
            typeof(PaymentCreatedEvent),
            typeof(PaymentCompletedEvent),
            typeof(PaymentFailedEvent),
            typeof(PaymentRefundedEvent)
        }));
    }

    [Test]
    public void AResendableNotification_IsKeyedByTheEventTypeTheLogRecords()
    {
        // NotificationConsumer stores typeof(TEvent).Name; the lookup must use exactly that string.
        Assert.Multiple(() =>
        {
            Assert.That(ResendableNotifications.TryGet(nameof(OrderCreatedEvent), out var order), Is.True);
            Assert.That(order.ConsumerType, Is.EqualTo(typeof(OrderCreatedConsumer)));
            Assert.That(ResendableNotifications.TryGet(nameof(PasswordResetRequestedIntegrationEvent), out _), Is.False);
            Assert.That(ResendableNotifications.TryGet("orderCreatedEvent", out _), Is.False, "ordinal, as stored");
        });
    }

    /// <summary>
    /// The queue a resend goes to must be the queue <c>ConfigureEndpoints</c> binds the consumer to — which carries the
    /// service prefix. The bare <c>order_created</c> is not Notification's; before <c>420a4da</c> it was shared with
    /// Payment's consumer of the same event, and a resend sent there would open a second payment.
    /// </summary>
    [Test]
    public void AResendGoesToTheConsumersOwnPrefixedQueue()
    {
        var formatter = MassTransitServiceCollectionExtensions.CreateEndpointNameFormatter(
            ServiceCollectionExtensions.MessagingServiceName);

        ResendableNotifications.TryGet(nameof(OrderCreatedEvent), out var order);
        ResendableNotifications.TryGet(nameof(PaymentRefundedEvent), out var refund);

        Assert.Multiple(() =>
        {
            Assert.That(order.QueueName, Is.EqualTo("notification_order_created"));
            Assert.That(order.QueueName, Is.EqualTo(formatter.Consumer<OrderCreatedConsumer>()));
            Assert.That(refund.QueueName, Is.EqualTo(formatter.Consumer<PaymentRefundedConsumer>()));
        });
    }
}
