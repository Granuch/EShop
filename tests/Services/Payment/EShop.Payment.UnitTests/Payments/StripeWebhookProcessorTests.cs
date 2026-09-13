using EShop.BuildingBlocks.Application.Abstractions;
using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Repositories;
using EShop.Payment.Infrastructure.Services;
using EShop.BuildingBlocks.Domain.Outbox;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.BuildingBlocks.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Payment.UnitTests.Payments;

[TestFixture]
public class StripeWebhookProcessorTests
{
    private static PaymentDbContext CreateDbContext(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options;

        return new PaymentDbContext(options);
    }

    [Test]
    public async Task ProcessAsync_WhenSucceededEvent_ShouldPersistOutboxMessages()
    {
        var dbName = Guid.NewGuid().ToString();

        await using (var writeDbContext = CreateDbContext(dbName))
        {
            var repository = new PaymentRepository(writeDbContext);

            await repository.AddAsync(new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                OrderId = Guid.NewGuid(),
                UserId = "user-1",
                Amount = 100m,
                Currency = "USD",
                PaymentMethod = PaymentMethodType.Stripe,
                PaymentIntentId = "pi_test_outbox_001",
                Status = PaymentStatus.Processing,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await writeDbContext.SaveChangesAsync();

            var eventParser = new Mock<IStripeWebhookEventParser>();
            eventParser.Setup(x => x.Parse(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(new StripeWebhookEvent(
                    "evt_test_outbox_001",
                    "payment_intent.succeeded",
                    "pi_test_outbox_001",
                    "succeeded",
                    null,
                    true));

            var processor = new StripeWebhookProcessor(
                repository,
                eventParser.Object,
                writeDbContext,
                new IntegrationEventOutbox(writeDbContext),
                Mock.Of<ILogger<StripeWebhookProcessor>>());

            var result = await processor.ProcessAsync("payload", "sig", CancellationToken.None);

            Assert.That(result.IsDuplicate, Is.False);
        }

        await using var readDbContext = CreateDbContext(dbName);
        var outboxMessages = await readDbContext.Set<OutboxMessage>().ToListAsync();

        Assert.That(outboxMessages, Has.Count.EqualTo(2));
        Assert.That(outboxMessages.Any(m => m.Type.EndsWith(nameof(PaymentSuccessEvent), StringComparison.Ordinal)), Is.True);
        Assert.That(outboxMessages.Any(m => m.Type.EndsWith(nameof(PaymentCompletedEvent), StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public async Task ProcessAsync_WhenSucceededEvent_ShouldMarkPaymentSuccess()
    {
        await using var dbContext = CreateDbContext();
        var repository = new PaymentRepository(dbContext);

        await repository.AddAsync(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "user-1",
            Amount = 100m,
            Currency = "USD",
            PaymentMethod = PaymentMethodType.Stripe,
            PaymentIntentId = "pi_test_001",
            Status = PaymentStatus.Processing,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var eventParser = new Mock<IStripeWebhookEventParser>();
        eventParser.Setup(x => x.Parse(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new StripeWebhookEvent("evt_test_001", "payment_intent.succeeded", "pi_test_001", "succeeded", null, true));

        var outbox = new Mock<IIntegrationEventOutbox>();

        var processor = new StripeWebhookProcessor(
            repository,
            eventParser.Object,
            dbContext,
            outbox.Object,
            Mock.Of<ILogger<StripeWebhookProcessor>>());

        var result = await processor.ProcessAsync("payload", "sig", CancellationToken.None);

        Assert.That(result.IsDuplicate, Is.False);

        var updated = await repository.GetByPaymentIntentIdAsync("pi_test_001", CancellationToken.None);
        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.Status, Is.EqualTo(PaymentStatus.Success));
        Assert.That(await repository.IsStripeEventProcessedAsync("evt_test_001", CancellationToken.None), Is.True);

        outbox.Verify(x => x.Enqueue(It.IsAny<EShop.BuildingBlocks.Messaging.Events.PaymentSuccessEvent>(), It.IsAny<string?>()), Times.Once);
        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentCompletedEvent>(), It.IsAny<string?>()), Times.Once);
    }

    /// <summary>
    /// Ordering audit Stage 9. After OrderCancelledConsumer cancels an intent, Stripe sends its own
    /// payment_intent.canceled (and a late payment_failed can still arrive). Neither may turn the
    /// Cancelled record into Failed or publish a PaymentFailedEvent for an already-cancelled order.
    /// </summary>
    [TestCase("payment_intent.canceled", "canceled")]
    [TestCase("payment_intent.payment_failed", "requires_payment_method")]
    public async Task ProcessAsync_ForAPaymentWeCancelled_KeepsItCancelled_AndPublishesNoFailure(string type, string status)
    {
        await using var dbContext = CreateDbContext();
        var repository = new PaymentRepository(dbContext);

        await repository.AddAsync(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "user-1",
            Amount = 100m,
            Currency = "USD",
            PaymentMethod = PaymentMethodType.Stripe,
            PaymentIntentId = "pi_cancelled_by_us",
            Status = PaymentStatus.Cancelled,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var eventParser = new Mock<IStripeWebhookEventParser>();
        eventParser.Setup(x => x.Parse(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new StripeWebhookEvent($"evt_{type}", type, "pi_cancelled_by_us", status, null, true));

        var outbox = new Mock<IIntegrationEventOutbox>();

        var processor = new StripeWebhookProcessor(
            repository,
            eventParser.Object,
            dbContext,
            outbox.Object,
            Mock.Of<ILogger<StripeWebhookProcessor>>());

        await processor.ProcessAsync("payload", "sig", CancellationToken.None);

        var stored = await repository.GetByPaymentIntentIdAsync("pi_cancelled_by_us", CancellationToken.None);
        Assert.That(stored!.Status, Is.EqualTo(PaymentStatus.Cancelled));
        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentFailedEvent>(), It.IsAny<string?>()), Times.Never);
    }

    /// <summary>
    /// Payment audit Stage 5 (H1, D3). A declined card ends an attempt, not the payment: Stripe leaves the intent
    /// payable with another card. It used to be recorded Failed with a PaymentFailedEvent, so Ordering cancelled the
    /// order while the customer could still pay it.
    /// </summary>
    [TestCase(PaymentStatus.Pending)]
    [TestCase(PaymentStatus.Processing)]
    public async Task ADecline_KeepsThePaymentOpen_RecordsWhy_AndPublishesNoFailure(PaymentStatus status)
    {
        await using var dbContext = CreateDbContext();
        var (processor, outbox, intentId) = await APaymentWithWebhooksAsync(dbContext, status,
            Decline("evt_decline"));

        await processor.ProcessAsync("payload", "sig", CancellationToken.None);

        var stored = await new PaymentRepository(dbContext).GetByPaymentIntentIdAsync(intentId, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(stored!.Status, Is.EqualTo(status));
            Assert.That(stored.ErrorMessage, Is.EqualTo("Your card was declined."));
            Assert.That(stored.StripeStatus, Is.EqualTo("requires_payment_method"));
            Assert.That(stored.ProcessedAt, Is.Null, "nothing was settled");
        });
        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentFailedEvent>(), It.IsAny<string?>()), Times.Never);
    }

    /// <summary>Stripe does not order deliveries: a decline of the first card can arrive after the second one paid.</summary>
    [Test]
    public async Task ADeclineDeliveredAfterTheSuccess_LeavesThePaymentSucceeded()
    {
        await using var dbContext = CreateDbContext();
        var (processor, outbox, intentId) = await APaymentWithWebhooksAsync(dbContext, PaymentStatus.Success,
            Decline("evt_late_decline"));

        await processor.ProcessAsync("payload", "sig", CancellationToken.None);

        var stored = await new PaymentRepository(dbContext).GetByPaymentIntentIdAsync(intentId, CancellationToken.None);
        Assert.That(stored!.Status, Is.EqualTo(PaymentStatus.Success));
        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentFailedEvent>(), It.IsAny<string?>()), Times.Never);
    }

    [Test]
    public async Task ASecondCardAfterADecline_SettlesThePayment_AndClearsTheError()
    {
        await using var dbContext = CreateDbContext();
        var (processor, outbox, intentId) = await APaymentWithWebhooksAsync(dbContext, PaymentStatus.Processing,
            Decline("evt_first_card"),
            new StripeWebhookEvent("evt_second_card", "payment_intent.succeeded", "pi_retry", "succeeded", null, true));

        await processor.ProcessAsync("payload", "sig", CancellationToken.None);
        await processor.ProcessAsync("payload", "sig", CancellationToken.None);

        var stored = await new PaymentRepository(dbContext).GetByPaymentIntentIdAsync(intentId, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(stored!.Status, Is.EqualTo(PaymentStatus.Success));
            Assert.That(stored.ErrorMessage, Is.Null);
        });
        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentSuccessEvent>(), It.IsAny<string?>()), Times.Once);
        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentFailedEvent>(), It.IsAny<string?>()), Times.Never);
    }

    private static StripeWebhookEvent Decline(string eventId) => new(
        eventId, "payment_intent.payment_failed", "pi_retry", "requires_payment_method", "Your card was declined.", true);

    /// <summary>A Stripe payment on intent <c>pi_retry</c>, and a processor whose parser yields <paramref name="events"/> in turn.</summary>
    private static async Task<(StripeWebhookProcessor Processor, Mock<IIntegrationEventOutbox> Outbox, string IntentId)> APaymentWithWebhooksAsync(
        PaymentDbContext dbContext, PaymentStatus status, params StripeWebhookEvent[] events)
    {
        var repository = new PaymentRepository(dbContext);
        await repository.AddAsync(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "user-1",
            Amount = 100m,
            Currency = "USD",
            PaymentMethod = PaymentMethodType.Stripe,
            PaymentIntentId = "pi_retry",
            Status = status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var eventParser = new Mock<IStripeWebhookEventParser>();
        var sequence = eventParser.SetupSequence(x => x.Parse(It.IsAny<string>(), It.IsAny<string>()));
        foreach (var stripeEvent in events)
        {
            sequence = sequence.Returns(stripeEvent);
        }

        var outbox = new Mock<IIntegrationEventOutbox>();
        var processor = new StripeWebhookProcessor(
            repository, eventParser.Object, dbContext, outbox.Object, Mock.Of<ILogger<StripeWebhookProcessor>>());
        return (processor, outbox, "pi_retry");
    }

    /// <summary>
    /// Ordering audit Stage 21 (D17). Stripe's canceled webhook arriving while the payment still reads
    /// Processing: OrderCancelledConsumer cancelled the intent and has not committed yet. The intent's tag
    /// decides it. Tagged, it is a cancelled order: Cancelled, no PaymentFailedEvent. Untagged (a Dashboard or
    /// Stripe cancellation), it is still a payment that failed, as before.
    /// </summary>
    [TestCase(true, PaymentStatus.Cancelled, 0)]
    [TestCase(false, PaymentStatus.Failed, 1)]
    public async Task ProcessAsync_CanceledWebhookOnALivePayment_IsACancellationOnlyWhenWeRequestedIt(
        bool cancelRequestedByEShop, PaymentStatus expectedStatus, int failuresPublished)
    {
        await using var dbContext = CreateDbContext();
        var repository = new PaymentRepository(dbContext);

        await repository.AddAsync(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "user-1",
            Amount = 100m,
            Currency = "USD",
            PaymentMethod = PaymentMethodType.Stripe,
            PaymentIntentId = "pi_live_canceled",
            Status = PaymentStatus.Processing,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var eventParser = new Mock<IStripeWebhookEventParser>();
        eventParser.Setup(x => x.Parse(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new StripeWebhookEvent(
                "evt_live_canceled", "payment_intent.canceled", "pi_live_canceled", "canceled", null, true,
                cancelRequestedByEShop));

        var outbox = new Mock<IIntegrationEventOutbox>();

        var processor = new StripeWebhookProcessor(
            repository,
            eventParser.Object,
            dbContext,
            outbox.Object,
            Mock.Of<ILogger<StripeWebhookProcessor>>());

        await processor.ProcessAsync("payload", "sig", CancellationToken.None);

        var stored = await repository.GetByPaymentIntentIdAsync("pi_live_canceled", CancellationToken.None);
        Assert.That(stored!.Status, Is.EqualTo(expectedStatus));
        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentFailedEvent>(), It.IsAny<string?>()), Times.Exactly(failuresPublished));
    }

    [Test]
    public async Task ProcessAsync_WhenDuplicateEvent_ShouldReturnDuplicate()
    {
        await using var dbContext = CreateDbContext();
        var repository = new PaymentRepository(dbContext);

        var eventParser = new Mock<IStripeWebhookEventParser>();
        eventParser.Setup(x => x.Parse(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new StripeWebhookEvent("evt_test_dup", "payment_intent.succeeded", "pi_missing", "succeeded", null, true));

        var processor = new StripeWebhookProcessor(
            repository,
            eventParser.Object,
            dbContext,
            Mock.Of<IIntegrationEventOutbox>(),
            Mock.Of<ILogger<StripeWebhookProcessor>>());

        await processor.ProcessAsync("payload", "sig", CancellationToken.None);
        var duplicate = await processor.ProcessAsync("payload", "sig", CancellationToken.None);

        Assert.That(duplicate.IsDuplicate, Is.True);
    }

    [Test]
    public async Task ProcessAsync_WhenUnsupportedEvent_ShouldIgnoreWithoutPersistence()
    {
        await using var dbContext = CreateDbContext();
        var repository = new PaymentRepository(dbContext);

        var eventParser = new Mock<IStripeWebhookEventParser>();
        eventParser.Setup(x => x.Parse(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new StripeWebhookEvent("evt_ignored", "charge.succeeded", string.Empty, string.Empty, null, false));

        var processor = new StripeWebhookProcessor(
            repository,
            eventParser.Object,
            dbContext,
            Mock.Of<IIntegrationEventOutbox>(),
            Mock.Of<ILogger<StripeWebhookProcessor>>());

        var result = await processor.ProcessAsync("payload", "sig", CancellationToken.None);

        Assert.That(result.IsDuplicate, Is.False);
        Assert.That(result.PaymentFound, Is.False);
        Assert.That(await repository.IsStripeEventProcessedAsync("evt_ignored", CancellationToken.None), Is.False);
    }
}
