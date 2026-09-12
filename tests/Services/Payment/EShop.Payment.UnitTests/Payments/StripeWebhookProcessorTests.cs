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
                PaymentMethod = "Stripe",
                PaymentIntentId = "pi_test_outbox_001",
                Status = PaymentStatus.Processing,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await writeDbContext.SaveChangesAsync();

            var stripePaymentService = new Mock<IStripePaymentService>();
            stripePaymentService.Setup(x => x.ConstructWebhookEvent(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(new StripeWebhookEvent(
                    "evt_test_outbox_001",
                    "payment_intent.succeeded",
                    "pi_test_outbox_001",
                    "succeeded",
                    null,
                    true));

            var processor = new StripeWebhookProcessor(
                repository,
                stripePaymentService.Object,
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
            PaymentMethod = "Stripe",
            PaymentIntentId = "pi_test_001",
            Status = PaymentStatus.Processing,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var stripePaymentService = new Mock<IStripePaymentService>();
        stripePaymentService.Setup(x => x.ConstructWebhookEvent(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new StripeWebhookEvent("evt_test_001", "payment_intent.succeeded", "pi_test_001", "succeeded", null, true));

        var outbox = new Mock<IIntegrationEventOutbox>();

        var processor = new StripeWebhookProcessor(
            repository,
            stripePaymentService.Object,
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
            PaymentMethod = "Stripe",
            PaymentIntentId = "pi_cancelled_by_us",
            Status = PaymentStatus.Cancelled,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var stripePaymentService = new Mock<IStripePaymentService>();
        stripePaymentService.Setup(x => x.ConstructWebhookEvent(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new StripeWebhookEvent($"evt_{type}", type, "pi_cancelled_by_us", status, null, true));

        var outbox = new Mock<IIntegrationEventOutbox>();

        var processor = new StripeWebhookProcessor(
            repository,
            stripePaymentService.Object,
            dbContext,
            outbox.Object,
            Mock.Of<ILogger<StripeWebhookProcessor>>());

        await processor.ProcessAsync("payload", "sig", CancellationToken.None);

        var stored = await repository.GetByPaymentIntentIdAsync("pi_cancelled_by_us", CancellationToken.None);
        Assert.That(stored!.Status, Is.EqualTo(PaymentStatus.Cancelled));
        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentFailedEvent>(), It.IsAny<string?>()), Times.Never);
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
            PaymentMethod = "Stripe",
            PaymentIntentId = "pi_live_canceled",
            Status = PaymentStatus.Processing,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var stripePaymentService = new Mock<IStripePaymentService>();
        stripePaymentService.Setup(x => x.ConstructWebhookEvent(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new StripeWebhookEvent(
                "evt_live_canceled", "payment_intent.canceled", "pi_live_canceled", "canceled", null, true,
                cancelRequestedByEShop));

        var outbox = new Mock<IIntegrationEventOutbox>();

        var processor = new StripeWebhookProcessor(
            repository,
            stripePaymentService.Object,
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

        var stripePaymentService = new Mock<IStripePaymentService>();
        stripePaymentService.Setup(x => x.ConstructWebhookEvent(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new StripeWebhookEvent("evt_test_dup", "payment_intent.succeeded", "pi_missing", "succeeded", null, true));

        var processor = new StripeWebhookProcessor(
            repository,
            stripePaymentService.Object,
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

        var stripePaymentService = new Mock<IStripePaymentService>();
        stripePaymentService.Setup(x => x.ConstructWebhookEvent(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new StripeWebhookEvent("evt_ignored", "charge.succeeded", string.Empty, string.Empty, null, false));

        var processor = new StripeWebhookProcessor(
            repository,
            stripePaymentService.Object,
            dbContext,
            Mock.Of<IIntegrationEventOutbox>(),
            Mock.Of<ILogger<StripeWebhookProcessor>>());

        var result = await processor.ProcessAsync("payload", "sig", CancellationToken.None);

        Assert.That(result.IsDuplicate, Is.False);
        Assert.That(result.PaymentFound, Is.False);
        Assert.That(await repository.IsStripeEventProcessedAsync("evt_ignored", CancellationToken.None), Is.False);
    }
}
