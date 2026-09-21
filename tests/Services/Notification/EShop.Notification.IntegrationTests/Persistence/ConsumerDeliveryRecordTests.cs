using EShop.BuildingBlocks.Messaging.Events;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Entities;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.Models;
using EShop.Notification.Domain.ValueObjects;
using EShop.Notification.Infrastructure.Configuration;
using EShop.Notification.Infrastructure.Consumers;
using EShop.Notification.Infrastructure.Data;
using EShop.Notification.Infrastructure.Repositories;
using EShop.Notification.IntegrationTests.Fixtures;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Notification.IntegrationTests.Persistence;

/// <summary>
/// Notification audit S1 (H2) and S2 (H1, H3; D1, D2, D5). A consumer delivered as production delivers it: the real
/// consumer and repository on PostgreSQL, each delivery on its own connection. On EF InMemory there is no row version,
/// no unique index and no transaction, which is how the old tests came to assert behaviour Postgres did not have.
///
/// <para>S1 pinned two defects here; S2 flipped them. A failed send used to leave no row, because the failure was written
/// inside <c>IdempotentConsumer</c>'s transaction and rolled back with it (H1). A sent email whose <c>Sent</c> save
/// failed used to be sent again on redelivery (H3).</para>
///
/// <para>Overlapping deliveries are staged, not timed (a timed race may never race): the first delivery is paused at the
/// point that matters, and the second runs to completion inside that pause.</para>
///
/// <para><see cref="PaymentRefundedConsumer"/> stands in for all seven consumers, which share
/// <see cref="NotificationConsumer{TEvent}"/>.</para>
/// </summary>
[TestFixture]
public class ConsumerDeliveryRecordTests
{
    private string _connectionString = null!;

    [SetUp]
    public async Task CreateDatabaseAsync() => _connectionString = await PostgresTestServer.CreateDatabaseAsync();

    [TearDown]
    public void ReleaseDatabase() => PostgresTestServer.ReleaseDatabase(_connectionString);

    [Test]
    public async Task ASuccessfulSend_LeavesOneSentRow()
    {
        var email = new CountingEmailService();
        var refund = Refund();

        await ConsumeAsync(refund, email);

        var log = await SingleLogAsync(refund.EventId);
        Assert.Multiple(() =>
        {
            Assert.That(email.Sends, Is.EqualTo(1));
            Assert.That(log.Status, Is.EqualTo(NotificationStatus.Sent));
            Assert.That(log.RecipientEmail, Is.EqualTo("user@test.com"));
            Assert.That(log.RetryCount, Is.Zero);
            Assert.That(log.SentAt, Is.Not.Null);
        });
    }

    [Test]
    public async Task ASendThatFails_PersistsAFailedRow_AndTheRetrySendsIt()
    {
        var email = new CountingEmailService { FailSends = true };
        var refund = Refund();

        Assert.ThrowsAsync<InvalidOperationException>(() => ConsumeAsync(refund, email));

        var failed = await SingleLogAsync(refund.EventId);
        Assert.Multiple(() =>
        {
            Assert.That(failed.Status, Is.EqualTo(NotificationStatus.Failed),
                "audit H1: the failure is recorded in its own commit and survives the throw (D1)");
            Assert.That(failed.RetryCount, Is.EqualTo(1));
            Assert.That(failed.LastError, Is.EqualTo("Email provider connectivity error."));
        });

        // MassTransit retries the message.
        email.FailSends = false;
        await ConsumeAsync(refund, email);

        var sent = await SingleLogAsync(refund.EventId);
        Assert.Multiple(() =>
        {
            Assert.That(email.Sends, Is.EqualTo(2));
            Assert.That(sent.Status, Is.EqualTo(NotificationStatus.Sent));
            Assert.That(sent.RetryCount, Is.EqualTo(1), "the failed attempt is still counted");
        });
    }

    [Test]
    public async Task ASentEmailWhoseSentSaveFails_IsAcknowledged_AndNotSentAgain()
    {
        var email = new CountingEmailService();
        var refund = Refund();
        FailingSentSaveRepository? failingSave = null;

        Assert.DoesNotThrowAsync(
            () => ConsumeAsync(refund, email, inner => failingSave = new FailingSentSaveRepository(inner)),
            "audit H3: once the email is out, a failure to record it is logged, not rethrown (D2)");
        Assert.That(failingSave?.SentSaveAttempted, Is.True, "precondition: the send succeeded and its Sent save failed");

        // A second copy of the event (an outbox republish) arrives while the attempt still holds its claim.
        Assert.ThrowsAsync<NotificationDeliveryInProgressException>(() => ConsumeAsync(refund, email));

        var log = await SingleLogAsync(refund.EventId);
        Assert.Multiple(() =>
        {
            Assert.That(email.Sends, Is.EqualTo(1));
            Assert.That(log.Status, Is.EqualTo(NotificationStatus.Sending), "the Sent state was never written");
        });
    }

    [Test]
    public async Task TheSameEventDeliveredTwice_SendsOneEmail()
    {
        var email = new CountingEmailService();
        var refund = Refund();

        await ConsumeAsync(refund, email);
        Assert.DoesNotThrowAsync(() => ConsumeAsync(refund, email), "a duplicate is acknowledged, not failed");

        var logs = await LogsAsync(refund.EventId);
        Assert.Multiple(() =>
        {
            Assert.That(email.Sends, Is.EqualTo(1));
            Assert.That(logs, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task ADeliveryThatFindsAnAttemptInProgress_IsRetriedLater_AndSendsNothing()
    {
        var refund = Refund();
        Exception? secondDelivery = null;
        var email = new CountingEmailService();
        email.DuringFirstSend = async () => secondDelivery = await CaptureAsync(() => ConsumeAsync(refund, email));

        await ConsumeAsync(refund, email);

        var log = await SingleLogAsync(refund.EventId);
        Assert.Multiple(() =>
        {
            Assert.That(secondDelivery, Is.InstanceOf<NotificationDeliveryInProgressException>());
            Assert.That(email.Sends, Is.EqualTo(1));
            Assert.That(log.Status, Is.EqualTo(NotificationStatus.Sent));
        });
    }

    /// <summary>An attempt that died with its process (D5): past the lease, the next delivery takes it over.</summary>
    [Test]
    public async Task AnAttemptOlderThanTheLease_IsTakenOver()
    {
        var refund = Refund();
        await SeedAsync(refund, log => log.BeginAttempt(
            DateTime.UtcNow - NotificationDelivery.AttemptLease - TimeSpan.FromMinutes(1)));
        var email = new CountingEmailService();

        await ConsumeAsync(refund, email);

        var log = await SingleLogAsync(refund.EventId);
        Assert.Multiple(() =>
        {
            Assert.That(email.Sends, Is.EqualTo(1));
            Assert.That(log.Status, Is.EqualTo(NotificationStatus.Sent));
        });
    }

    /// <summary>
    /// Both deliveries read the row as Pending. The second one claims it, sends and records Sent while the first is
    /// paused; the first then tries to claim the state it read, and its UPDATE matches no row (D5).
    /// </summary>
    [Test]
    public async Task TwoDeliveriesThatReadTheSamePendingRow_OnlyOneClaimsIt()
    {
        var refund = Refund();
        await SeedAsync(refund, _ => { });
        var email = new CountingEmailService();
        var otherDeliveryCommitted = false;

        Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ConsumeAsync(
            refund,
            email,
            inner => new PausingAfterReadRepository(inner, async () =>
            {
                await ConsumeAsync(refund, email);
                otherDeliveryCommitted = true;
            })));

        var log = await SingleLogAsync(refund.EventId);
        Assert.Multiple(() =>
        {
            Assert.That(otherDeliveryCommitted, Is.True, "precondition: the other delivery ran inside the pause");
            Assert.That(email.Sends, Is.EqualTo(1));
            Assert.That(log.Status, Is.EqualTo(NotificationStatus.Sent), "the first writer's state stands");
        });
    }

    /// <summary>
    /// S3 (M1, D3). A permanent failure is acknowledged instead of being retried into the error queue and the circuit
    /// breaker, and it stays final: a later copy of the event sends nothing even once Identity would answer.
    /// </summary>
    [Test]
    public async Task ARecipientIdentityDoesNotKnow_IsRecordedUndeliverable_AndNeverRetried()
    {
        var email = new CountingEmailService();
        var refund = Refund();

        Assert.DoesNotThrowAsync(() => ConsumeAsync(
            refund, email, lookup: RecipientLookup.Undeliverable("Identity has no such user (404).")));
        await ConsumeAsync(refund, email);

        var log = await SingleLogAsync(refund.EventId);
        Assert.Multiple(() =>
        {
            Assert.That(email.Sends, Is.Zero);
            Assert.That(log.Status, Is.EqualTo(NotificationStatus.Undeliverable));
            Assert.That(log.LastError, Is.EqualTo("Identity has no such user (404)."));
            Assert.That(log.RetryCount, Is.Zero);
        });
    }

    private NotificationDbContext NewContext() => new(new DbContextOptionsBuilder<NotificationDbContext>()
        .UseNpgsql(_connectionString)
        .Options);

    private async Task ConsumeAsync(
        PaymentRefundedEvent message,
        IEmailService email,
        Func<INotificationLogRepository, INotificationLogRepository>? decorate = null,
        RecipientLookup? lookup = null)
    {
        await using var db = NewContext();
        INotificationLogRepository repository = new NotificationLogRepository(db);
        if (decorate is not null)
        {
            repository = decorate(repository);
        }

        var consumer = new PaymentRefundedConsumer(
            repository,
            email,
            new FixedResolver(lookup ?? RecipientLookup.Found(new RecipientAddress("user@test.com", "User"))),
            Options.Create(new SmtpSettings { FromEmail = "support@eshop.local" }),
            TimeProvider.System,
            NullLogger<PaymentRefundedConsumer>.Instance);

        await consumer.Consume(Delivery(message));
    }

    private async Task SeedAsync(PaymentRefundedEvent refund, Action<NotificationLog> prepare)
    {
        await using var db = NewContext();
        var log = NotificationLog.CreatePending(
            refund.EventId, nameof(PaymentRefundedEvent), null, refund.UserId, "payment-refunded", "seeded");
        prepare(log);
        db.NotificationLogs.Add(log);
        await db.SaveChangesAsync();
    }

    private async Task<List<NotificationLog>> LogsAsync(Guid eventId)
    {
        await using var db = NewContext();
        return await db.NotificationLogs.AsNoTracking().Where(l => l.EventId == eventId).ToListAsync();
    }

    private async Task<NotificationLog> SingleLogAsync(Guid eventId) => (await LogsAsync(eventId)).Single();

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static PaymentRefundedEvent Refund() => new()
    {
        EventId = Guid.NewGuid(),
        OrderId = Guid.NewGuid(),
        UserId = "user-1",
        PaymentIntentId = "pi_delivery_record_1",
        Amount = 25m,
        RefundedAt = DateTime.UtcNow
    };

    private static ConsumeContext<PaymentRefundedEvent> Delivery(PaymentRefundedEvent message)
    {
        var context = new Mock<ConsumeContext<PaymentRefundedEvent>>();
        context.SetupGet(x => x.Message).Returns(message);
        context.SetupGet(x => x.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    private sealed class FixedResolver(RecipientLookup lookup) : IUserContactResolver
    {
        public Task<RecipientLookup> ResolveAsync(string userId, CancellationToken ct = default) => Task.FromResult(lookup);
    }

    /// <summary>Fails the first save of a <c>Sent</c> log, as a dropped connection after the SMTP send would.</summary>
    private sealed class FailingSentSaveRepository(INotificationLogRepository inner) : INotificationLogRepository
    {
        public bool SentSaveAttempted { get; private set; }

        public Task SaveAsync(NotificationLog log, CancellationToken ct = default)
        {
            if (log.Status == NotificationStatus.Sent && !SentSaveAttempted)
            {
                SentSaveAttempted = true;
                throw new DbUpdateException("Simulated failure saving the Sent state.");
            }

            return inner.SaveAsync(log, ct);
        }

        public Task<bool> TryAddAsync(NotificationLog log, CancellationToken ct = default) => inner.TryAddAsync(log, ct);

        public Task<NotificationLog?> FindByEventIdAsync(Guid eventId, CancellationToken ct = default)
            => inner.FindByEventIdAsync(eventId, ct);

        public Task<NotificationLog?> FindByIdAsync(Guid id, CancellationToken ct = default) => inner.FindByIdAsync(id, ct);
    }

    /// <summary>Runs <c>pause</c> once, after the first read and before anything is written.</summary>
    private sealed class PausingAfterReadRepository(INotificationLogRepository inner, Func<Task> pause)
        : INotificationLogRepository
    {
        private Func<Task>? _pause = pause;

        public async Task<NotificationLog?> FindByEventIdAsync(Guid eventId, CancellationToken ct = default)
        {
            var log = await inner.FindByEventIdAsync(eventId, ct);
            var pauseOnce = Interlocked.Exchange(ref _pause, null);
            if (pauseOnce is not null)
            {
                await pauseOnce();
            }

            return log;
        }

        public Task<bool> TryAddAsync(NotificationLog log, CancellationToken ct = default) => inner.TryAddAsync(log, ct);

        public Task SaveAsync(NotificationLog log, CancellationToken ct = default) => inner.SaveAsync(log, ct);

        public Task<NotificationLog?> FindByIdAsync(Guid id, CancellationToken ct = default) => inner.FindByIdAsync(id, ct);
    }

    private sealed class CountingEmailService : IEmailService
    {
        private Func<Task>? _duringFirstSend;

        public bool FailSends { get; set; }
        public int Sends { get; private set; }

        /// <summary>Runs once, inside the first send, before it counts.</summary>
        public Func<Task>? DuringFirstSend
        {
            set => _duringFirstSend = value;
        }

        public async Task<string> SendPaymentRefundedAsync(RecipientAddress recipient, PaymentRefundedEmailModel model, CancellationToken ct = default)
        {
            var pause = Interlocked.Exchange(ref _duringFirstSend, null);
            if (pause is not null)
            {
                await pause();
            }

            Sends++;
            if (FailSends)
            {
                throw new InvalidOperationException("Simulated SMTP refund failure");
            }

            return $"refund-{Sends}@test.local";
        }

        public Task<string> SendOrderConfirmationAsync(RecipientAddress recipient, OrderConfirmationEmailModel model, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string> SendOrderShippedAsync(RecipientAddress recipient, OrderShippedEmailModel model, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string> SendPaymentCreatedAsync(RecipientAddress recipient, PaymentCreatedEmailModel model, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string> SendPaymentCompletedAsync(RecipientAddress recipient, PaymentCompletedEmailModel model, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string> SendPaymentFailedAsync(RecipientAddress recipient, PaymentFailedEmailModel model, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string> SendPasswordResetAsync(RecipientAddress recipient, PasswordResetEmailModel model, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
