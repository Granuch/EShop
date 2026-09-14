using EShop.BuildingBlocks.Infrastructure.Consumers;
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
/// Notification audit S1 (H2). A consumer delivered as production delivers it: the real consumer on PostgreSQL,
/// inside <c>IdempotentConsumer</c>'s real transaction and <c>ON CONFLICT</c> message claim, each delivery on its own
/// connection. On EF InMemory the base class takes a different, non-transactional branch, which is how the old tests
/// came to assert a persisted <c>Failed</c> row that Postgres never keeps.
///
/// <para><b>Two of these tests pin defects as they are today</b>, so that S2 flips them visibly rather than
/// silently:</para>
/// <list type="bullet">
///   <item>H1: a failed send leaves no row at all, because the consumer writes <c>Failed</c> and then throws, and the
///   throw rolls the write back together with the claim.</item>
///   <item>H3: a sent email whose <c>Sent</c> save fails is sent again on redelivery, because the send happened
///   inside the transaction that rolled back.</item>
/// </list>
/// <para>S2 changes both (Notification audit D1, D2); their assertion messages say so.</para>
///
/// <para><see cref="PaymentRefundedConsumer"/> stands in for all seven consumers, which share this flow line for
/// line.</para>
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
    public async Task ASuccessfulSend_LeavesOneSentRow_AndTheMessageClaim()
    {
        var email = new CountingEmailService();
        var refund = Refund();
        var messageId = Guid.NewGuid();

        await ConsumeAsync(Delivery(refund, messageId), email);

        var logs = await LogsAsync(refund.EventId);
        var claims = await ClaimCountAsync(messageId);
        Assert.Multiple(() =>
        {
            Assert.That(email.Sends, Is.EqualTo(1));
            Assert.That(logs, Has.Count.EqualTo(1));
            Assert.That(logs[0].Status, Is.EqualTo(NotificationStatus.Sent));
            Assert.That(claims, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ASendThatFails_LeavesNoRowAndNoClaim_BecauseTheFailureWriteRollsBack()
    {
        var email = new CountingEmailService { FailSends = true };
        var refund = Refund();
        var messageId = Guid.NewGuid();

        Assert.ThrowsAsync<InvalidOperationException>(() => ConsumeAsync(Delivery(refund, messageId), email));

        var logs = await LogsAsync(refund.EventId);
        var claims = await ClaimCountAsync(messageId);
        Assert.Multiple(() =>
        {
            Assert.That(email.Sends, Is.EqualTo(1));
            Assert.That(logs, Is.Empty,
                "audit H1 as it is today: the Failed row is rolled back with the claim. S2 (D1) makes it persist");
            Assert.That(claims, Is.Zero, "the claim rolled back too, so MassTransit's retry is not skipped");
        });
    }

    [Test]
    public async Task ASentEmailWhoseSentSaveFails_IsSentAgainOnRedelivery()
    {
        var email = new CountingEmailService();
        var refund = Refund();
        var messageId = Guid.NewGuid();
        FailingSentSaveRepository? failingSave = null;

        Assert.ThrowsAsync<DbUpdateException>(() => ConsumeAsync(
            Delivery(refund, messageId),
            email,
            inner => failingSave = new FailingSentSaveRepository(inner)));

        Assert.That(failingSave?.SentSaveAttempted, Is.True, "precondition: the send succeeded and its Sent save failed");
        var logsAfterFailure = await LogsAsync(refund.EventId);
        var claimsAfterFailure = await ClaimCountAsync(messageId);
        Assert.Multiple(() =>
        {
            Assert.That(email.Sends, Is.EqualTo(1));
            Assert.That(logsAfterFailure, Is.Empty, "nothing records that the email went out");
            Assert.That(claimsAfterFailure, Is.Zero);
        });

        // MassTransit redelivers the same message.
        await ConsumeAsync(Delivery(refund, messageId), email);

        var logs = await LogsAsync(refund.EventId);
        Assert.Multiple(() =>
        {
            Assert.That(email.Sends, Is.EqualTo(2),
                "audit H3 as it is today: the customer gets the refund email twice. S2 (D2) records Sent in its own commit");
            Assert.That(logs, Has.Count.EqualTo(1));
            Assert.That(logs[0].Status, Is.EqualTo(NotificationStatus.Sent));
        });
    }

    /// <summary>
    /// The second delivery carries a different <c>EventId</c>, so the consumer's own <c>NotificationLogs</c> look-up
    /// cannot be what stops it: only the relational <c>processed_messages</c> claim can.
    /// </summary>
    [Test]
    public async Task TheSameMessageDeliveredTwice_SendsOneEmail()
    {
        var email = new CountingEmailService();
        var messageId = Guid.NewGuid();
        var first = Refund();
        var second = first with { EventId = Guid.NewGuid() };

        await ConsumeAsync(Delivery(first, messageId), email);
        Assert.DoesNotThrowAsync(() => ConsumeAsync(Delivery(second, messageId), email),
            "a duplicate is acknowledged, not failed");

        var secondLogs = await LogsAsync(second.EventId);
        var claims = await ClaimCountAsync(messageId);
        Assert.Multiple(() =>
        {
            Assert.That(email.Sends, Is.EqualTo(1));
            Assert.That(secondLogs, Is.Empty);
            Assert.That(claims, Is.EqualTo(1));
        });
    }

    private NotificationDbContext NewContext() => new(new DbContextOptionsBuilder<NotificationDbContext>()
        .UseNpgsql(_connectionString)
        .Options);

    private async Task ConsumeAsync(
        ConsumeContext<PaymentRefundedEvent> delivery,
        IEmailService email,
        Func<INotificationLogRepository, INotificationLogRepository>? decorate = null)
    {
        await using var db = NewContext();
        INotificationLogRepository repository = new NotificationLogRepository(db);
        if (decorate is not null)
        {
            repository = decorate(repository);
        }

        var consumer = new PaymentRefundedConsumer(
            db,
            repository,
            email,
            new FixedResolver(),
            Options.Create(new SmtpSettings { FromEmail = "support@eshop.local" }),
            NullLogger<PaymentRefundedConsumer>.Instance);

        await consumer.Consume(delivery);
    }

    private async Task<List<NotificationLog>> LogsAsync(Guid eventId)
    {
        await using var db = NewContext();
        return await db.NotificationLogs.AsNoTracking().Where(l => l.EventId == eventId).ToListAsync();
    }

    private async Task<int> ClaimCountAsync(Guid messageId)
    {
        await using var db = NewContext();
        return await db.Set<ProcessedMessage>().CountAsync(m => m.MessageId == messageId);
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

    private static ConsumeContext<PaymentRefundedEvent> Delivery(PaymentRefundedEvent message, Guid messageId)
    {
        var context = new Mock<ConsumeContext<PaymentRefundedEvent>>();
        context.SetupGet(x => x.Message).Returns(message);
        context.SetupGet(x => x.MessageId).Returns(messageId);
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    private sealed class FixedResolver : IUserContactResolver
    {
        public Task<RecipientAddress?> ResolveAsync(string userId, CancellationToken ct = default)
            => Task.FromResult<RecipientAddress?>(new RecipientAddress("user@test.com", "User"));
    }

    /// <summary>Fails the first save of a <c>Sent</c> log, as a dropped connection after the SMTP send would.</summary>
    private sealed class FailingSentSaveRepository(INotificationLogRepository inner) : INotificationLogRepository
    {
        public bool SentSaveAttempted { get; private set; }

        public Task UpdateAsync(NotificationLog log, CancellationToken ct = default)
        {
            if (log.Status == NotificationStatus.Sent && !SentSaveAttempted)
            {
                SentSaveAttempted = true;
                throw new DbUpdateException("Simulated failure saving the Sent state.");
            }

            return inner.UpdateAsync(log, ct);
        }

        public Task AddAsync(NotificationLog log, CancellationToken ct = default) => inner.AddAsync(log, ct);

        public Task<NotificationLog?> FindByEventIdAsync(Guid eventId, CancellationToken ct = default)
            => inner.FindByEventIdAsync(eventId, ct);
    }

    private sealed class CountingEmailService : IEmailService
    {
        public bool FailSends { get; init; }
        public int Sends { get; private set; }

        public Task SendPaymentRefundedAsync(RecipientAddress recipient, PaymentRefundedEmailModel model, CancellationToken ct = default)
        {
            Sends++;
            return FailSends
                ? throw new InvalidOperationException("Simulated SMTP refund failure")
                : Task.CompletedTask;
        }

        public Task SendOrderConfirmationAsync(RecipientAddress recipient, OrderConfirmationEmailModel model, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task SendOrderShippedAsync(RecipientAddress recipient, OrderShippedEmailModel model, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task SendPaymentCreatedAsync(RecipientAddress recipient, PaymentCreatedEmailModel model, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task SendPaymentCompletedAsync(RecipientAddress recipient, PaymentCompletedEmailModel model, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task SendPaymentFailedAsync(RecipientAddress recipient, PaymentFailedEmailModel model, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task SendPasswordResetAsync(RecipientAddress recipient, PasswordResetEmailModel model, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
