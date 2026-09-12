using EShop.BuildingBlocks.Infrastructure.Consumers;
using EShop.BuildingBlocks.Infrastructure.Services;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Application.Payments.Refunds;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Configuration;
using EShop.Payment.Infrastructure.Consumers;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Repositories;
using EShop.Payment.Infrastructure.Services;
using EShop.Payment.IntegrationTests.Fixtures;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Payment.IntegrationTests.Persistence;

/// <summary>
/// Ordering audit Stage 20. A cancelled order's consumer and a Stripe webhook write the same payment row at
/// the same instant: both have read it at the same version, and each decides on what it read. On EF
/// InMemory (the unit suite, <c>LateSuccessWebhookTests</c>) the two can only run one after the other. Here
/// they genuinely overlap, on PostgreSQL, through the real consumer, webhook processor, refunder and
/// outbox, each on its own connection as in production.
///
/// <para><b>What decides the collision is the database</b>: <c>PaymentTransaction.Version</c> is mapped to
/// <c>xmin</c> as a row version, so EF's <c>UPDATE … WHERE "Id" = @id AND xmin = @read</c> matches nothing
/// for whichever writer comes second and throws <see cref="DbUpdateConcurrencyException"/>. Without it the
/// second writer silently overwrites the first. Each test also follows the loser's retry, because that retry,
/// re-reading the winner's state, is what makes the rejection safe:</para>
/// <list type="bullet">
///   <item>the webhook loses → its whole save rolls back, including the processed-event row, so the endpoint
///   answers 500 and Stripe's redelivery is processed on its merits instead of being taken for a duplicate;</item>
///   <item>the consumer loses → <c>IdempotentConsumer</c>'s transaction rolls back its message claim with it, so
///   MassTransit's retry (a <see cref="DbUpdateConcurrencyException"/> is not an <see cref="ArgumentException"/>)
///   is not skipped as a duplicate.</item>
/// </list>
///
/// <para>The overlap is staged, not timed (a timed race may never race): the webhook is paused after it has
/// decided and before it writes, or the consumer is paused while it is waiting on Stripe, and the other writer
/// runs to its commit in that gap.</para>
///
/// <para>Stripe's own <c>payment_intent.canceled</c> webhook committing <i>before</i> the consumer that
/// cancelled the intent used to end the payment Failed, with a "payment failed" email (audit N6). Since Stage
/// 21 (D17) the consumer tags the intent before cancelling it, and the webhook records a tagged cancellation
/// as Cancelled; both orderings are pinned below.</para>
/// </summary>
[TestFixture]
public class PaymentWriteCollisionTests
{
    private const string IntentId = "pi_collision_1";
    private const decimal Amount = 40m;
    private const string Currency = "USD";

    private string _connectionString = null!;

    [SetUp]
    public async Task CreateDatabaseAsync() => _connectionString = await PostgresTestServer.CreateDatabaseAsync();

    [TearDown]
    public void ReleaseDatabase() => PostgresTestServer.ReleaseDatabase(_connectionString);

    /// <summary>
    /// A captured payment is refunded by the cancellation while Stripe's success webhook is being processed.
    /// The refund commits first; the webhook, which read the payment as Processing and decided Success,
    /// writes second.
    /// </summary>
    [Test]
    public async Task ARefundAndASuccessWebhook_OnOneVersion_TheWebhookWritingSecondIsRejected_AndItsRedeliveryKeepsTheRefund()
    {
        var payment = await SeedStripePaymentAsync(PaymentStatus.Processing);
        var stripe = StripeWithTheIntentAlreadySucceeded();
        var cancellation = Cancellation(payment.OrderId, Guid.NewGuid());
        var cancellationCommitted = false;

        Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () => await DeliverWebhookAsync(
            "evt_succeeded_1", "payment_intent.succeeded", "succeeded",
            beforeItsWrite: async () =>
            {
                await ConsumeCancellationAsync(cancellation, stripe.Object, autoRefund: true);
                cancellationCommitted = true;
            }));

        Assert.That(cancellationCommitted, Is.True, "precondition: the refund committed inside the webhook's window");
        Assert.Multiple(async () =>
        {
            Assert.That((await ReadPaymentAsync(payment.Id)).Status, Is.EqualTo(PaymentStatus.Refunded),
                "the first writer's state stands");
            Assert.That(await OutboxCountAsync<PaymentRefundedEvent>(), Is.EqualTo(1));
            Assert.That(await OutboxCountAsync<PaymentSuccessEvent>(), Is.Zero, "the rejected webhook sent nothing");
            Assert.That(await OutboxCountAsync<PaymentCompletedEvent>(), Is.Zero);
            Assert.That(await WebhookRecordedAsync("evt_succeeded_1"), Is.False,
                "the event is not recorded, so Stripe's redelivery is not a duplicate");
        });

        // The endpoint answered 500, so Stripe delivers the same event again.
        var redelivered = await DeliverWebhookAsync("evt_succeeded_1", "payment_intent.succeeded", "succeeded");

        Assert.Multiple(async () =>
        {
            Assert.That(redelivered.IsDuplicate, Is.False);
            Assert.That((await ReadPaymentAsync(payment.Id)).Status, Is.EqualTo(PaymentStatus.Refunded));
            Assert.That(await OutboxCountAsync<PaymentSuccessEvent>(), Is.Zero);
            Assert.That(await WebhookRecordedAsync("evt_succeeded_1"), Is.True);
        });
    }

    /// <summary>
    /// The other order. The consumer has read the payment as Processing and is asking Stripe whether the
    /// intent succeeded; in that moment the success webhook records Success and commits. The consumer then
    /// refunds and writes second.
    /// </summary>
    [Test]
    public async Task ARefundAndASuccessWebhook_OnOneVersion_TheRefundWritingSecondIsRejected_AndItsRetryRefundsTheRecordedSuccess()
    {
        var payment = await SeedStripePaymentAsync(PaymentStatus.Processing);
        var messageId = Guid.NewGuid();
        var cancellation = Cancellation(payment.OrderId, messageId);
        var webhookCommitted = false;

        var stripe = StripeWithTheIntentAlreadySucceeded();
        stripe.Setup(s => s.GetPaymentIntentStatusAsync(IntentId, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await DeliverWebhookAsync("evt_succeeded_1", "payment_intent.succeeded", "succeeded");
                webhookCommitted = true;
                return "succeeded";
            });

        Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () =>
            await ConsumeCancellationAsync(cancellation, stripe.Object, autoRefund: true));

        Assert.That(webhookCommitted, Is.True, "precondition: the webhook committed inside the consumer's window");
        Assert.Multiple(async () =>
        {
            Assert.That((await ReadPaymentAsync(payment.Id)).Status, Is.EqualTo(PaymentStatus.Success),
                "the first writer's state stands");
            Assert.That(await OutboxCountAsync<PaymentSuccessEvent>(), Is.EqualTo(1));
            Assert.That(await OutboxCountAsync<PaymentRefundedEvent>(), Is.Zero, "the rejected refund recorded nothing");
            Assert.That(await ClaimedAsync(messageId), Is.False,
                "the claim rolled back with the write, so the retry is not a duplicate");
        });

        // MassTransit retries the same message.
        await ConsumeCancellationAsync(cancellation, stripe.Object, autoRefund: true);

        Assert.Multiple(async () =>
        {
            Assert.That((await ReadPaymentAsync(payment.Id)).Status, Is.EqualTo(PaymentStatus.Refunded));
            Assert.That(await OutboxCountAsync<PaymentRefundedEvent>(), Is.EqualTo(1));
            Assert.That(await ClaimedAsync(messageId), Is.True);
        });

        // Both attempts asked Stripe for the refund. StripePaymentService sends both under the key
        // refund-{intent}, which Stripe replays rather than refunding twice
        // (StripeSandboxTests.OurRefund_RepeatedWithinTheIdempotencyWindow_ReturnsTheSameRefund).
        stripe.Verify(s => s.CreateRefundAsync(IntentId, Amount, Currency, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>
    /// A Pending order is cancelled: the consumer cancels the intent at Stripe and records Cancelled. Stripe's
    /// own <c>payment_intent.canceled</c> webhook had already read the payment as Processing and decided
    /// Failed; it writes second.
    /// </summary>
    [Test]
    public async Task ACancellationAndStripesCanceledWebhook_OnOneVersion_TheWebhookWritingSecondIsRejected_AndItsRedeliveryKeepsItCancelled()
    {
        var payment = await SeedStripePaymentAsync(PaymentStatus.Processing);
        var stripe = new Mock<IStripePaymentService>(MockBehavior.Strict);
        stripe.Setup(s => s.CancelPaymentIntentAsync(IntentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripePaymentIntentCancelResult(IntentId, "canceled"));
        var cancellation = Cancellation(payment.OrderId, Guid.NewGuid());
        var cancellationCommitted = false;

        Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () => await DeliverWebhookAsync(
            "evt_canceled_1", "payment_intent.canceled", "canceled",
            beforeItsWrite: async () =>
            {
                await ConsumeCancellationAsync(cancellation, stripe.Object, autoRefund: false);
                cancellationCommitted = true;
            },
            cancelRequestedByEShop: true));

        Assert.That(cancellationCommitted, Is.True, "precondition: the cancellation committed inside the webhook's window");
        Assert.Multiple(async () =>
        {
            Assert.That((await ReadPaymentAsync(payment.Id)).Status, Is.EqualTo(PaymentStatus.Cancelled),
                "the first writer's state stands");
            Assert.That(await OutboxCountAsync<PaymentFailedEvent>(), Is.Zero, "the rejected webhook sent nothing");
            Assert.That(await WebhookRecordedAsync("evt_canceled_1"), Is.False);
        });

        var redelivered = await DeliverWebhookAsync(
            "evt_canceled_1", "payment_intent.canceled", "canceled", cancelRequestedByEShop: true);

        Assert.Multiple(async () =>
        {
            Assert.That(redelivered.IsDuplicate, Is.False);
            Assert.That((await ReadPaymentAsync(payment.Id)).Status, Is.EqualTo(PaymentStatus.Cancelled));
            Assert.That(await OutboxCountAsync<PaymentFailedEvent>(), Is.Zero);
        });
    }

    /// <summary>
    /// Stage 21 (D17), the ordering that was audit N6. The consumer has cancelled the intent at Stripe and not
    /// yet committed; Stripe's canceled webhook, carrying the consumer's tag, commits first. It records
    /// Cancelled and sends no PaymentFailedEvent. The consumer then loses on the row version, and its retry
    /// finds the payment Cancelled: nothing to do.
    /// </summary>
    [Test]
    public async Task ACancellationAndStripesCanceledWebhook_OnOneVersion_TheCancellationWritingSecondIsRejected_AndThePaymentEndsCancelled()
    {
        var payment = await SeedStripePaymentAsync(PaymentStatus.Processing);
        var messageId = Guid.NewGuid();
        var cancellation = Cancellation(payment.OrderId, messageId);
        var webhookCommitted = false;

        var stripe = new Mock<IStripePaymentService>(MockBehavior.Strict);
        stripe.Setup(s => s.CancelPaymentIntentAsync(IntentId, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                if (!webhookCommitted)
                {
                    await DeliverWebhookAsync(
                        "evt_canceled_1", "payment_intent.canceled", "canceled", cancelRequestedByEShop: true);
                    webhookCommitted = true;
                }

                return new StripePaymentIntentCancelResult(IntentId, "canceled");
            });

        Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () =>
            await ConsumeCancellationAsync(cancellation, stripe.Object, autoRefund: false));

        Assert.That(webhookCommitted, Is.True, "precondition: the webhook committed inside the consumer's window");
        Assert.Multiple(async () =>
        {
            Assert.That((await ReadPaymentAsync(payment.Id)).Status, Is.EqualTo(PaymentStatus.Cancelled),
                "the webhook recorded the cancellation, not a failure");
            Assert.That(await OutboxCountAsync<PaymentFailedEvent>(), Is.Zero, "no \"payment failed\" email");
            Assert.That(await ClaimedAsync(messageId), Is.False);
        });

        // MassTransit retries the same message.
        await ConsumeCancellationAsync(cancellation, stripe.Object, autoRefund: false);

        Assert.Multiple(async () =>
        {
            Assert.That((await ReadPaymentAsync(payment.Id)).Status, Is.EqualTo(PaymentStatus.Cancelled));
            Assert.That(await OutboxCountAsync<PaymentFailedEvent>(), Is.Zero);
            Assert.That(await ClaimedAsync(messageId), Is.True);
        });
    }

    private PaymentDbContext NewContext() => new(new DbContextOptionsBuilder<PaymentDbContext>()
        .UseNpgsql(_connectionString)
        .Options);

    private async Task<PaymentTransaction> SeedStripePaymentAsync(PaymentStatus status)
    {
        await using var db = NewContext();
        var payment = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "user-1",
            Amount = Amount,
            Currency = Currency,
            PaymentMethod = "Stripe",
            PaymentIntentId = IntentId,
            Status = status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.PaymentTransactions.Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }

    /// <summary>Stripe refuses the cancel because the intent succeeded, confirms it, and refunds it.</summary>
    private static Mock<IStripePaymentService> StripeWithTheIntentAlreadySucceeded()
    {
        var stripe = new Mock<IStripePaymentService>(MockBehavior.Strict);
        stripe.Setup(s => s.CancelPaymentIntentAsync(IntentId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PaymentIntentNotCancellableException(IntentId, "This PaymentIntent's status is succeeded."));
        stripe.Setup(s => s.GetPaymentIntentStatusAsync(IntentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("succeeded");
        stripe.Setup(s => s.CreateRefundAsync(IntentId, Amount, Currency, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeRefundResult("re_collision_1", "succeeded"));
        return stripe;
    }

    /// <summary>
    /// The cancellation as MassTransit delivers it: the real consumer on its own connection, inside
    /// <c>IdempotentConsumer</c>'s real transaction and <c>ON CONFLICT</c> claim.
    /// </summary>
    private async Task ConsumeCancellationAsync(
        ConsumeContext<OrderCancelledEvent> message,
        IStripePaymentService stripe,
        bool autoRefund)
    {
        await using var db = NewContext();
        var refunder = new PaymentRefunder(
            new PaymentRepository(db),
            new Mock<IPaymentProcessor>(MockBehavior.Strict).Object,
            stripe,
            new IntegrationEventOutbox(db),
            NullLogger<PaymentRefunder>.Instance);
        var consumer = new OrderCancelledConsumer(
            db,
            new PaymentRepository(db),
            db,
            stripe,
            refunder,
            Options.Create(new CancelledOrderRefundSettings { AutoRefund = autoRefund }),
            NullLogger<OrderCancelledConsumer>.Instance);

        await consumer.Consume(message);
    }

    /// <summary>
    /// One webhook through the real processor on its own connection. <paramref name="beforeItsWrite"/> runs
    /// once the processor has read the payment and decided what to write, and before it writes.
    /// </summary>
    private async Task<StripeWebhookProcessResult> DeliverWebhookAsync(
        string eventId,
        string eventType,
        string intentStatus,
        Func<Task>? beforeItsWrite = null,
        bool cancelRequestedByEShop = false)
    {
        await using var db = NewContext();
        var stripe = new Mock<IStripePaymentService>(MockBehavior.Strict);
        stripe.Setup(s => s.ConstructWebhookEvent(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new StripeWebhookEvent(eventId, eventType, IntentId, intentStatus, null, true, cancelRequestedByEShop));

        IPaymentRepository repository = new PaymentRepository(db);
        if (beforeItsWrite is not null)
        {
            repository = new PausingBeforeUpdateRepository(repository, beforeItsWrite);
        }

        var processor = new StripeWebhookProcessor(
            repository, stripe.Object, db, new IntegrationEventOutbox(db), NullLogger<StripeWebhookProcessor>.Instance);
        return await processor.ProcessAsync("payload", "signature", CancellationToken.None);
    }

    private async Task<PaymentTransaction> ReadPaymentAsync(Guid paymentId)
    {
        await using var db = NewContext();
        return await db.PaymentTransactions.AsNoTracking().SingleAsync(p => p.Id == paymentId);
    }

    private async Task<int> OutboxCountAsync<TEvent>()
    {
        var type = typeof(TEvent).FullName!;
        await using var db = NewContext();
        return await db.OutboxMessages.CountAsync(m => m.Type == type);
    }

    private async Task<bool> WebhookRecordedAsync(string eventId)
    {
        await using var db = NewContext();
        return await db.ProcessedStripeWebhookEvents.AnyAsync(e => e.EventId == eventId);
    }

    private async Task<bool> ClaimedAsync(Guid messageId)
    {
        await using var db = NewContext();
        return await db.Set<ProcessedMessage>().AnyAsync(m => m.MessageId == messageId);
    }

    private static ConsumeContext<OrderCancelledEvent> Cancellation(Guid orderId, Guid messageId)
    {
        var context = new Mock<ConsumeContext<OrderCancelledEvent>>();
        context.SetupGet(x => x.Message).Returns(new OrderCancelledEvent
        {
            OrderId = orderId,
            UserId = "user-1",
            Reason = "changed my mind"
        });
        context.SetupGet(x => x.MessageId).Returns(messageId);
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    /// <summary>
    /// The webhook processor calls <see cref="IPaymentRepository.UpdateAsync"/> only once it has decided to
    /// change the payment, on what it read, and before it saves: the window the other writer runs in.
    /// </summary>
    private sealed class PausingBeforeUpdateRepository(IPaymentRepository inner, Func<Task> pause) : IPaymentRepository
    {
        private Func<Task>? _pause = pause;

        public async Task UpdateAsync(PaymentTransaction payment, CancellationToken cancellationToken = default)
        {
            var pauseOnce = Interlocked.Exchange(ref _pause, null);
            if (pauseOnce is not null)
            {
                await pauseOnce();
            }

            await inner.UpdateAsync(payment, cancellationToken);
        }

        public Task<PaymentTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => inner.GetByIdAsync(id, cancellationToken);

        public Task<PaymentTransaction?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
            => inner.GetByOrderIdAsync(orderId, cancellationToken);

        public Task<PaymentTransaction?> GetByPaymentIntentIdAsync(string paymentIntentId, CancellationToken cancellationToken = default)
            => inner.GetByPaymentIntentIdAsync(paymentIntentId, cancellationToken);

        public Task<List<PaymentTransaction>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default)
            => inner.GetByUserIdAsync(userId, cancellationToken);

        public Task<PaymentCustomer?> GetCustomerByUserIdAsync(string userId, CancellationToken cancellationToken = default)
            => inner.GetCustomerByUserIdAsync(userId, cancellationToken);

        public Task AddCustomerAsync(PaymentCustomer customer, CancellationToken cancellationToken = default)
            => inner.AddCustomerAsync(customer, cancellationToken);

        public Task<bool> IsStripeEventProcessedAsync(string eventId, CancellationToken cancellationToken = default)
            => inner.IsStripeEventProcessedAsync(eventId, cancellationToken);

        public Task AddProcessedStripeEventAsync(ProcessedStripeWebhookEvent processedEvent, CancellationToken cancellationToken = default)
            => inner.AddProcessedStripeEventAsync(processedEvent, cancellationToken);

        public Task AddAsync(PaymentTransaction payment, CancellationToken cancellationToken = default)
            => inner.AddAsync(payment, cancellationToken);

        public IQueryable<PaymentTransaction> Query() => inner.Query();
    }
}
