using System.Text.Json;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Entities;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.Models;
using EShop.Notification.Domain.ValueObjects;
using EShop.Notification.Infrastructure.Configuration;
using EShop.Notification.Infrastructure.Consumers;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Notification.UnitTests.Consumers;

/// <summary>
/// The seven consumers over an in-memory log (<see cref="FakeLogs"/>). What only a real database can decide — the row
/// version, the unique index, what survives a throw — is in the integration suite's <c>ConsumerDeliveryRecordTests</c>.
/// </summary>
[TestFixture]
public class NotificationConsumerTests
{
    private static readonly IOptions<SmtpSettings> Smtp =
        Options.Create(new SmtpSettings { FromEmail = "support@eshop.local" });

    private static readonly IOptions<PasswordResetSettings> Reset =
        Options.Create(new PasswordResetSettings { ResetUrlBase = "https://frontend/reset-password" });

    private FakeLogs _logs = null!;
    private Mock<IEmailService> _email = null!;
    private Mock<IUserContactResolver> _resolver = null!;

    [SetUp]
    public void SetUp()
    {
        _logs = new FakeLogs();
        _email = new Mock<IEmailService>();
        _resolver = new Mock<IUserContactResolver>();
    }

    [Test]
    public async Task OrderCreatedConsumer_WhenRecipientResolved_SendsAndRecordsSent()
    {
        var evt = new OrderCreatedEvent { EventId = Guid.NewGuid(), OrderId = Guid.NewGuid(), UserId = "user-1", TotalAmount = 125.50m };
        ResolveAs("user-1", "user1@test.com", "User One");

        await OrderCreated().Consume(Delivery(evt));

        _email.Verify(x => x.SendOrderConfirmationAsync(
            It.Is<RecipientAddress>(r => r.Email == "user1@test.com"),
            It.IsAny<OrderConfirmationEmailModel>(),
            It.IsAny<CancellationToken>()), Times.Once);
        var log = _logs.Single();
        Assert.Multiple(() =>
        {
            Assert.That(log.Status, Is.EqualTo(NotificationStatus.Sent));
            Assert.That(log.RecipientEmail, Is.EqualTo("user1@test.com"));
            Assert.That(_logs.Saves, Is.EqualTo(new[] { NotificationStatus.Sending, NotificationStatus.Sent }),
                "the claim is committed before the send, and Sent after it");
        });
    }

    [Test]
    public async Task OrderCreatedConsumer_WhenAlreadySent_SkipsSend()
    {
        var evt = new OrderCreatedEvent { EventId = Guid.NewGuid(), OrderId = Guid.NewGuid(), UserId = "user-2", TotalAmount = 10m };
        _logs.Seed(SentLog(evt.EventId));

        await OrderCreated().Consume(Delivery(evt));

        _email.Verify(x => x.SendOrderConfirmationAsync(
            It.IsAny<RecipientAddress>(), It.IsAny<OrderConfirmationEmailModel>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Multiple(() =>
        {
            Assert.That(_logs.Adds, Is.Zero);
            Assert.That(_logs.Saves, Is.Empty);
        });
    }

    [Test]
    public async Task OrderCreatedConsumer_WhenAnEarlierAttemptFailed_RetriesIt()
    {
        var evt = new OrderCreatedEvent { EventId = Guid.NewGuid(), OrderId = Guid.NewGuid(), UserId = "user-3", TotalAmount = 10m };
        _logs.Seed(FailedLog(evt.EventId));
        ResolveAs("user-3", "user3@test.com");

        await OrderCreated().Consume(Delivery(evt));

        _email.Verify(x => x.SendOrderConfirmationAsync(
            It.IsAny<RecipientAddress>(), It.IsAny<OrderConfirmationEmailModel>(), It.IsAny<CancellationToken>()), Times.Once);
        var log = _logs.Single();
        Assert.Multiple(() =>
        {
            Assert.That(log.Status, Is.EqualTo(NotificationStatus.Sent));
            Assert.That(log.RetryCount, Is.EqualTo(1), "the earlier failure stays counted");
        });
    }

    [Test]
    public async Task OrderShippedConsumer_WhenUserEmailPresent_ShouldBypassResolver()
    {
        var evt = new OrderShippedEvent
        {
            EventId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "user-3",
            UserEmail = "user3@test.com",
            TrackingNumber = "TRK123",
            ShippedAt = DateTime.UtcNow
        };

        await OrderShipped().Consume(Delivery(evt));

        _resolver.Verify(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _email.Verify(x => x.SendOrderShippedAsync(
            It.Is<RecipientAddress>(r => r.Email == "user3@test.com"),
            It.IsAny<OrderShippedEmailModel>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// S3 (M1, D3). A user Identity does not know used to be retried 16 times, calling Identity up to 48 times and
    /// counting toward the circuit breaker. It is now recorded as final and acknowledged.
    /// </summary>
    [Test]
    public void PaymentFailedConsumer_WhenIdentityHasNoSuchUser_RecordsUndeliverable_AndIsNotRetried()
    {
        var evt = new PaymentFailedEvent
        {
            EventId = Guid.NewGuid(), OrderId = Guid.NewGuid(), UserId = "user-4", Reason = "Card declined", FailedAt = DateTime.UtcNow
        };
        _resolver.Setup(x => x.ResolveAsync("user-4", It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecipientLookup.Undeliverable("Identity has no such user (404)."));

        Assert.DoesNotThrowAsync(() => PaymentFailed().Consume(Delivery(evt)));

        _email.Verify(x => x.SendPaymentFailedAsync(
            It.IsAny<RecipientAddress>(), It.IsAny<PaymentFailedEmailModel>(), It.IsAny<CancellationToken>()), Times.Never);
        var log = _logs.Single();
        Assert.Multiple(() =>
        {
            Assert.That(log.Status, Is.EqualTo(NotificationStatus.Undeliverable));
            Assert.That(log.RetryCount, Is.Zero);
            Assert.That(log.LastError, Is.EqualTo("Identity has no such user (404)."));
            Assert.That(log.RecipientEmail, Is.Null);
        });
    }

    [Test]
    public async Task AnUndeliverableNotification_IsNotAttemptedAgain()
    {
        var evt = new OrderCreatedEvent { EventId = Guid.NewGuid(), OrderId = Guid.NewGuid(), UserId = "user-4", TotalAmount = 1m };
        var undeliverable = NotificationLog.CreatePending(evt.EventId, "Event", null, "user-4", "template", "subject");
        undeliverable.BeginAttempt(DateTime.UtcNow);
        undeliverable.MarkUndeliverable("Identity has no such user (404).");
        _logs.Seed(undeliverable);
        ResolveAs("user-4", "user4@test.com");

        await OrderCreated().Consume(Delivery(evt));

        _resolver.Verify(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.That(_logs.Saves, Is.Empty);
    }

    /// <summary>
    /// M2. Retrying cannot supply a missing UserId, so every consumer now records the failure and acknowledges the
    /// message; only PaymentFailedConsumer used to, and the other six retried it 16 times.
    /// </summary>
    [Test]
    public void OrderCreatedConsumer_WithoutAUserId_RecordsUndeliverable_AndIsNotRetried()
    {
        var evt = new OrderCreatedEvent { EventId = Guid.NewGuid(), OrderId = Guid.NewGuid(), UserId = "  ", TotalAmount = 1m };

        Assert.DoesNotThrowAsync(() => OrderCreated().Consume(Delivery(evt)));

        _resolver.Verify(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _email.Verify(x => x.SendOrderConfirmationAsync(
            It.IsAny<RecipientAddress>(), It.IsAny<OrderConfirmationEmailModel>(), It.IsAny<CancellationToken>()), Times.Never);
        var log = _logs.Single();
        Assert.Multiple(() =>
        {
            Assert.That(log.Status, Is.EqualTo(NotificationStatus.Undeliverable));
            Assert.That(log.LastError, Is.EqualTo("UserId is missing from the event."));
            Assert.That(log.RetryCount, Is.Zero);
        });
    }

    [Test]
    public void PaymentFailedConsumer_WithoutAUserId_RecordsUndeliverable_AndIsNotRetried()
    {
        var evt = new PaymentFailedEvent
        {
            EventId = Guid.NewGuid(), OrderId = Guid.NewGuid(), UserId = null, Reason = "Card declined", FailedAt = DateTime.UtcNow
        };

        Assert.DoesNotThrowAsync(() => PaymentFailed().Consume(Delivery(evt)));

        _resolver.Verify(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.That(_logs.Single().Status, Is.EqualTo(NotificationStatus.Undeliverable));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OrderCreatedConsumer_WhenIdentityIsUnavailable_RecordsFailedAndRethrows(bool isConfigurationError)
    {
        var evt = new OrderCreatedEvent { EventId = Guid.NewGuid(), OrderId = Guid.NewGuid(), UserId = "user-5", TotalAmount = 1m };
        _resolver.Setup(x => x.ResolveAsync("user-5", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UserContactUnavailableException("Identity answered 503. Gave up after 3 attempts.", isConfigurationError));

        Assert.ThrowsAsync<UserContactUnavailableException>(() => OrderCreated().Consume(Delivery(evt)));

        _email.Verify(x => x.SendOrderConfirmationAsync(
            It.IsAny<RecipientAddress>(), It.IsAny<OrderConfirmationEmailModel>(), It.IsAny<CancellationToken>()), Times.Never);
        var log = _logs.Single();
        Assert.Multiple(() =>
        {
            Assert.That(log.Status, Is.EqualTo(NotificationStatus.Failed), "a later attempt may succeed, so it is retried");
            Assert.That(log.RetryCount, Is.EqualTo(1));
        });
    }

    /// <summary>L3. A send failure whose message is empty used to turn into MarkFailed's ArgumentException.</summary>
    [Test]
    public void OrderCreatedConsumer_WhenTheSendFailsWithAnEmptyMessage_RethrowsTheOriginalException()
    {
        var evt = new OrderCreatedEvent { EventId = Guid.NewGuid(), OrderId = Guid.NewGuid(), UserId = "user-6", TotalAmount = 1m };
        ResolveAs("user-6", "user6@test.com");
        var original = new IOException(string.Empty);
        _email.Setup(x => x.SendOrderConfirmationAsync(
                It.IsAny<RecipientAddress>(), It.IsAny<OrderConfirmationEmailModel>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(original);

        var thrown = Assert.ThrowsAsync<IOException>(() => OrderCreated().Consume(Delivery(evt)));

        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.SameAs(original));
            Assert.That(_logs.Single().Status, Is.EqualTo(NotificationStatus.Failed));
        });
    }

    /// <summary>M3. The stored and logged correlation id is the payload's, as IdempotentConsumer resolves it.</summary>
    [Test]
    public async Task TheLogStoresThePayloadCorrelationId_NotTheTransportHeader()
    {
        var evt = new OrderCreatedEvent
        {
            EventId = Guid.NewGuid(), CorrelationId = "corr-from-payload", OrderId = Guid.NewGuid(), UserId = "user-7", TotalAmount = 1m
        };
        ResolveAs("user-7", "user7@test.com");

        await OrderCreated().Consume(Delivery(evt, transportCorrelationId: Guid.NewGuid()));

        Assert.That(_logs.Single().CorrelationId, Is.EqualTo("corr-from-payload"));
    }

    [Test]
    public async Task WithoutAPayloadCorrelationId_TheLogStoresTheTransportHeader()
    {
        var header = Guid.NewGuid();
        var evt = new OrderCreatedEvent { EventId = Guid.NewGuid(), OrderId = Guid.NewGuid(), UserId = "user-8", TotalAmount = 1m };
        ResolveAs("user-8", "user8@test.com");

        await OrderCreated().Consume(Delivery(evt, transportCorrelationId: header));

        Assert.That(_logs.Single().CorrelationId, Is.EqualTo(header.ToString()));
    }

    [Test]
    public void AnEventWithoutAnEventId_IsRejectedWithoutARetry()
    {
        var evt = new OrderCreatedEvent { EventId = Guid.Empty, OrderId = Guid.NewGuid(), UserId = "user-9", TotalAmount = 1m };

        // ArgumentException is on the bus's no-retry list.
        Assert.ThrowsAsync<ArgumentException>(() => OrderCreated().Consume(Delivery(evt)));
        Assert.That(_logs.Adds, Is.Zero);
    }

    [Test]
    public async Task PaymentRefundedConsumer_WhenRecipientResolved_ShouldSendAndMarkSent()
    {
        var evt = new PaymentRefundedEvent
        {
            EventId = Guid.NewGuid(), OrderId = Guid.NewGuid(), UserId = "user-5", PaymentIntentId = "pi_ref_1", Amount = 42.5m, RefundedAt = DateTime.UtcNow
        };
        ResolveAs("user-5", "user5@test.com", "User Five");

        await PaymentRefunded().Consume(Delivery(evt));

        _email.Verify(x => x.SendPaymentRefundedAsync(
            It.IsAny<RecipientAddress>(), It.IsAny<PaymentRefundedEmailModel>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(_logs.Single().Status, Is.EqualTo(NotificationStatus.Sent));
    }

    #region Payment audit Stage 8b: the refund email's currency comes from the event

    /// <summary>
    /// The refund email used to hard-code USD whatever the event said. EUR is not the event's default, so a consumer
    /// that ignored the field would still say USD here and go red.
    /// </summary>
    [Test]
    public async Task PaymentRefundedConsumer_EmailsTheCurrencyTheEventCarries()
    {
        var model = await RefundEmailFor(new PaymentRefundedEvent
        {
            EventId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "user-11",
            PaymentIntentId = "pi_ref_eur",
            Amount = 42.5m,
            Currency = "EUR",
            RefundedAt = DateTime.UtcNow
        });

        Assert.Multiple(() =>
        {
            Assert.That(model.Currency, Is.EqualTo("EUR"));
            Assert.That(model.Amount, Is.EqualTo(42.5m));
        });
    }

    /// <summary>
    /// A PaymentRefundedEvent published before Stage 8b has no currency field. It may still be in Payment's outbox or in
    /// the queue during the deploy, and must read as USD, which is what Payment refunded.
    /// </summary>
    [Test]
    public async Task PaymentRefundedConsumer_AMessagePublishedBeforeTheCurrencyExisted_EmailsUsd()
    {
        var json = $$"""{"eventId":"{{Guid.NewGuid()}}","orderId":"{{Guid.NewGuid()}}","userId":"user-12","paymentIntentId":"pi_legacy","amount":10.5,"refundedAt":"2026-01-01T00:00:00Z"}""";
        var evt = JsonSerializer.Deserialize<PaymentRefundedEvent>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var model = await RefundEmailFor(evt);

        Assert.That(model.Currency, Is.EqualTo("USD"));
    }

    /// <summary>Consumes <paramref name="evt"/> and returns the refund email model the consumer sent.</summary>
    private async Task<PaymentRefundedEmailModel> RefundEmailFor(PaymentRefundedEvent evt)
    {
        ResolveAs(evt.UserId, "refund@test.com", "Refund Customer");

        PaymentRefundedEmailModel? sent = null;
        _email.Setup(x => x.SendPaymentRefundedAsync(
                It.IsAny<RecipientAddress>(), It.IsAny<PaymentRefundedEmailModel>(), It.IsAny<CancellationToken>()))
            .Callback<RecipientAddress, PaymentRefundedEmailModel, CancellationToken>((_, model, _) => sent = model)
            .ReturnsAsync("refund@test.local");

        await PaymentRefunded().Consume(Delivery(evt));

        return sent ?? throw new AssertionException("No refund email was sent.");
    }

    #endregion

    [Test]
    public async Task PaymentRefundedConsumer_WhenAlreadySent_ShouldSkipSend()
    {
        var evt = new PaymentRefundedEvent
        {
            EventId = Guid.NewGuid(), OrderId = Guid.NewGuid(), UserId = "user-6", PaymentIntentId = "pi_ref_2", Amount = 12m, RefundedAt = DateTime.UtcNow
        };
        _logs.Seed(SentLog(evt.EventId));

        await PaymentRefunded().Consume(Delivery(evt));

        _email.Verify(x => x.SendPaymentRefundedAsync(
            It.IsAny<RecipientAddress>(), It.IsAny<PaymentRefundedEmailModel>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.That(_logs.Adds, Is.Zero);
    }

    [Test]
    public async Task PaymentCreatedConsumer_WhenRecipientResolved_ShouldSendAndMarkSent()
    {
        var evt = new PaymentCreatedEvent
        {
            EventId = Guid.NewGuid(), OrderId = Guid.NewGuid(), UserId = "user-8", Amount = 18m, Currency = "USD", Status = "PENDING", CreatedAt = DateTime.UtcNow
        };
        ResolveAs("user-8", "user8@test.com", "User Eight");

        await PaymentCreated().Consume(Delivery(evt));

        _email.Verify(x => x.SendPaymentCreatedAsync(
            It.IsAny<RecipientAddress>(), It.IsAny<PaymentCreatedEmailModel>(), It.IsAny<CancellationToken>()), Times.Once);

        // Payment audit Stage 8 (M5). PaymentCreatedEvent means an attempt started with nothing charged yet, and a Stripe
        // customer may never finish paying. It used to be logged, and emailed, as "Payment received".
        Assert.That(_logs.Single().Subject, Is.EqualTo($"Payment started for order #{evt.OrderId}"));
    }

    [Test]
    public async Task PaymentCompletedConsumer_WhenRecipientResolved_ShouldSendAndMarkSent()
    {
        var evt = new PaymentCompletedEvent
        {
            EventId = Guid.NewGuid(), OrderId = Guid.NewGuid(), UserId = "user-9", Amount = 19m, Currency = "USD", PaymentIntentId = "pi_complete", CompletedAt = DateTime.UtcNow
        };
        ResolveAs("user-9", "user9@test.com", "User Nine");

        await PaymentCompleted().Consume(Delivery(evt));

        _email.Verify(x => x.SendPaymentCompletedAsync(
            It.IsAny<RecipientAddress>(), It.IsAny<PaymentCompletedEmailModel>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(_logs.Single().Status, Is.EqualTo(NotificationStatus.Sent));
    }

    [Test]
    public async Task PasswordResetRequestedConsumer_WhenRecipientResolved_ShouldSendAndMarkSent()
    {
        var evt = new PasswordResetRequestedIntegrationEvent { EventId = Guid.NewGuid(), UserId = "user-reset", ResetToken = "token-value" };
        ResolveAs("user-reset", "reset@test.com", "Reset User");

        await PasswordReset(Reset).Consume(Delivery(evt));

        _email.Verify(x => x.SendPasswordResetAsync(
            It.IsAny<RecipientAddress>(),
            It.Is<PasswordResetEmailModel>(m => m.ResetLink.Contains("userId=user-reset", StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(_logs.Single().Status, Is.EqualTo(NotificationStatus.Sent));
    }

    [Test]
    public void PasswordResetRequestedConsumer_WhenEmailSendFails_ShouldStoreSanitizedError()
    {
        var evt = new PasswordResetRequestedIntegrationEvent { EventId = Guid.NewGuid(), UserId = "user-reset-failed", ResetToken = "token-value" };
        ResolveAs("user-reset-failed", "reset@test.com", "Reset User");
        _email.Setup(x => x.SendPasswordResetAsync(
                It.IsAny<RecipientAddress>(), It.IsAny<PasswordResetEmailModel>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp provider timeout at internal.mailtrap.local:2525"));

        Assert.ThrowsAsync<InvalidOperationException>(() => PasswordReset(Reset).Consume(Delivery(evt)));

        var log = _logs.Single();
        Assert.Multiple(() =>
        {
            Assert.That(log.Status, Is.EqualTo(NotificationStatus.Failed));
            Assert.That(log.LastError, Is.EqualTo("Email provider timeout."));
        });
    }

    /// <summary>Notification audit S7 (L17, D11): the total's currency comes from the event, and the count is units.</summary>
    [Test]
    public async Task OrderCreatedConsumer_EmailsTheEventsCurrency_AndCountsUnitsNotLines()
    {
        var evt = new OrderCreatedEvent
        {
            EventId = Guid.NewGuid(), OrderId = Guid.NewGuid(), UserId = "user-20", TotalAmount = 30m, Currency = "EUR",
            Items =
            [
                new OrderEventItem { ProductName = "Widget", Price = 10m, Quantity = 2, SubTotal = 20m },
                new OrderEventItem { ProductName = "Gadget", Price = 10m, Quantity = 1, SubTotal = 10m }
            ]
        };
        ResolveAs("user-20", "user20@test.com", "Ada");

        var sent = await OrderConfirmationFor(evt);

        Assert.Multiple(() =>
        {
            Assert.That(sent.Currency, Is.EqualTo("EUR"));
            Assert.That(sent.ItemCount, Is.EqualTo(3), "two lines, three units");
            Assert.That(sent.CustomerName, Is.EqualTo("Ada"));
        });
    }

    /// <summary>D11: an OrderCreatedEvent published before the field existed still reads as USD, which it was.</summary>
    [Test]
    public async Task OrderCreatedConsumer_AMessagePublishedBeforeTheCurrencyExisted_EmailsUsd()
    {
        var json = $$"""{"eventId":"{{Guid.NewGuid()}}","orderId":"{{Guid.NewGuid()}}","userId":"user-21","totalAmount":12.5,"items":[]}""";
        var evt = JsonSerializer.Deserialize<OrderCreatedEvent>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        ResolveAs("user-21", "user21@test.com");

        var sent = await OrderConfirmationFor(evt);

        Assert.That(sent.Currency, Is.EqualTo("USD"));
    }

    /// <summary>S7 (L16): a customer Identity has no name for was greeted with their user id, a GUID.</summary>
    [Test]
    public async Task WithoutADisplayName_TheGreetingIsThere_NotTheUserId()
    {
        var evt = new OrderCreatedEvent { EventId = Guid.NewGuid(), OrderId = Guid.NewGuid(), UserId = "3fa85f64-5717-4562-b3fc-2c963f66afa6", TotalAmount = 1m };
        ResolveAs(evt.UserId, "nameless@test.com");

        var sent = await OrderConfirmationFor(evt);

        Assert.That(sent.CustomerName, Is.EqualTo("there"));
    }

    /// <summary>S7 (L21): the sender's Message-ID is kept, so the log row can be matched to the mail server's.</summary>
    [Test]
    public async Task TheMessageIdTheSenderReturns_IsRecordedAsTheProviderMessageId()
    {
        var evt = new OrderCreatedEvent { EventId = Guid.NewGuid(), OrderId = Guid.NewGuid(), UserId = "user-22", TotalAmount = 1m };
        ResolveAs("user-22", "user22@test.com");

        await OrderConfirmationFor(evt, messageId: "20260914.abc@eshop.local");

        Assert.That(_logs.Single().ProviderMessageId, Is.EqualTo("20260914.abc@eshop.local"));
    }

    private async Task<OrderConfirmationEmailModel> OrderConfirmationFor(OrderCreatedEvent evt, string messageId = "msg@test.local")
    {
        OrderConfirmationEmailModel? sent = null;
        _email.Setup(x => x.SendOrderConfirmationAsync(
                It.IsAny<RecipientAddress>(), It.IsAny<OrderConfirmationEmailModel>(), It.IsAny<CancellationToken>()))
            .Callback<RecipientAddress, OrderConfirmationEmailModel, CancellationToken>((_, model, _) => sent = model)
            .ReturnsAsync(messageId);

        await OrderCreated().Consume(Delivery(evt));

        return sent ?? throw new AssertionException("No order confirmation was sent.");
    }

    private void ResolveAs(string userId, string email, string? displayName = null)
        => _resolver.Setup(x => x.ResolveAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecipientLookup.Found(new RecipientAddress(email, displayName)));

    private OrderCreatedConsumer OrderCreated()
        => new(_logs, _email.Object, _resolver.Object, TimeProvider.System, Mock.Of<ILogger<OrderCreatedConsumer>>());

    private OrderShippedConsumer OrderShipped()
        => new(_logs, _email.Object, _resolver.Object, TimeProvider.System, Mock.Of<ILogger<OrderShippedConsumer>>());

    private PaymentCreatedConsumer PaymentCreated()
        => new(_logs, _email.Object, _resolver.Object, TimeProvider.System, Mock.Of<ILogger<PaymentCreatedConsumer>>());

    private PaymentCompletedConsumer PaymentCompleted()
        => new(_logs, _email.Object, _resolver.Object, TimeProvider.System, Mock.Of<ILogger<PaymentCompletedConsumer>>());

    private PaymentFailedConsumer PaymentFailed()
        => new(_logs, _email.Object, _resolver.Object, Smtp, TimeProvider.System, Mock.Of<ILogger<PaymentFailedConsumer>>());

    private PaymentRefundedConsumer PaymentRefunded()
        => new(_logs, _email.Object, _resolver.Object, Smtp, TimeProvider.System, Mock.Of<ILogger<PaymentRefundedConsumer>>());

    private PasswordResetRequestedConsumer PasswordReset(IOptions<PasswordResetSettings> settings)
        => new(_logs, _email.Object, _resolver.Object, settings, TimeProvider.System, Mock.Of<ILogger<PasswordResetRequestedConsumer>>());

    private static ConsumeContext<T> Delivery<T>(T message, Guid? transportCorrelationId = null)
        where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.SetupGet(x => x.Message).Returns(message);
        context.SetupGet(x => x.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(x => x.CorrelationId).Returns(transportCorrelationId);
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    private static NotificationLog SentLog(Guid eventId)
    {
        var log = NotificationLog.CreatePending(eventId, "Event", null, "user", "template", "subject");
        log.BeginAttempt(DateTime.UtcNow);
        log.MarkSent(providerMessageId: null);
        return log;
    }

    private static NotificationLog FailedLog(Guid eventId)
    {
        var log = NotificationLog.CreatePending(eventId, "Event", null, "user", "template", "subject");
        log.BeginAttempt(DateTime.UtcNow);
        log.MarkFailed("Simulated SMTP failure");
        return log;
    }

    /// <summary>The log store, keyed by EventId as the unique index keys it; records each saved status.</summary>
    private sealed class FakeLogs : INotificationLogRepository
    {
        private readonly Dictionary<Guid, NotificationLog> _rows = new();

        public List<NotificationStatus> Saves { get; } = new();
        public int Adds { get; private set; }

        public void Seed(NotificationLog log) => _rows[log.EventId] = log;

        public NotificationLog Single() => _rows.Values.Single();

        public Task<NotificationLog?> FindByEventIdAsync(Guid eventId, CancellationToken ct = default)
            => Task.FromResult(_rows.GetValueOrDefault(eventId));

        public Task<bool> TryAddAsync(NotificationLog log, CancellationToken ct = default)
        {
            if (_rows.ContainsKey(log.EventId))
            {
                return Task.FromResult(false);
            }

            _rows[log.EventId] = log;
            Adds++;
            return Task.FromResult(true);
        }

        public Task SaveAsync(NotificationLog log, CancellationToken ct = default)
        {
            Saves.Add(log.Status);
            return Task.CompletedTask;
        }
    }
}
