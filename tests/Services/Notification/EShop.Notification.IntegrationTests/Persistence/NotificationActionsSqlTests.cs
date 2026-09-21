using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Application.Notifications.Commands.MarkNotificationUndeliverable;
using EShop.Notification.Domain.Entities;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.Models;
using EShop.Notification.Domain.ValueObjects;
using EShop.Notification.Infrastructure.Configuration;
using EShop.Notification.Infrastructure.Consumers;
using EShop.Notification.Infrastructure.Data;
using EShop.Notification.Infrastructure.QueryServices;
using EShop.Notification.Infrastructure.Repositories;
using EShop.Notification.Infrastructure.Resend;
using EShop.Notification.IntegrationTests.Fixtures;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Notification.IntegrationTests.Persistence;

/// <summary>
/// Admin panel S13 on real PostgreSQL: what the HTTP suite's EF InMemory host cannot show. The batch query's SQL (seeded
/// dates need <c>ExecuteUpdate</c>, which is relational-only), the payload column's type, and — the one that matters
/// most — an operator's write losing to a delivery on the <c>xmin</c> row version, which InMemory does not have.
/// </summary>
[TestFixture]
public class NotificationActionsSqlTests
{
    private static readonly DateTime Day = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    private string _connectionString = null!;

    [SetUp]
    public async Task CreateDatabaseAsync() => _connectionString = await PostgresTestServer.CreateDatabaseAsync();

    [TearDown]
    public void ReleaseDatabase() => PostgresTestServer.ReleaseDatabase(_connectionString);

    [Test]
    public async Task TheRetryQuery_TakesOnlyFailedRowsThatKeptTheirEvent_OldestFirst()
    {
        // Inserted newest first, so insertion order and the answer disagree unless the query really sorts by CreatedAt.
        await SeedAsync(Failed(payload: true), Day.AddHours(3));
        var middle = await SeedAsync(Failed(payload: true), Day.AddHours(2));
        var oldest = await SeedAsync(Failed(payload: true), Day.AddHours(1));
        await SeedAsync(Failed(payload: false), Day);
        await SeedAsync(Sent(), Day);
        await SeedAsync(Pending(), Day);

        await using var db = NewContext();
        var (items, total) = await new NotificationQueryService(db)
            .GetRetryCandidatesAsync(NotificationJournalFilter.All, limit: 2);

        Assert.Multiple(() =>
        {
            Assert.That(total, Is.EqualTo(3), "failed, and kept its event — nothing else is a candidate");
            Assert.That(items.Select(i => i.Id), Is.EqualTo(new[] { oldest, middle }));
            Assert.That(items.All(i => i.EventType == nameof(PaymentRefundedEvent) && i.Payload.Length > 0), Is.True);
        });
    }

    /// <summary>
    /// "Retry the failed ones" must mean exactly that, whatever statuses the filter carries: narrowing by the caller's
    /// statuses could turn it into "retry nothing", and an empty set into "retry everything".
    /// </summary>
    [Test]
    public async Task TheRetryQuery_IgnoresTheFiltersOwnStatuses()
    {
        var failed = await SeedAsync(Failed(payload: true), Day);
        await SeedAsync(Sent(), Day);

        await using var db = NewContext();
        var (items, _) = await new NotificationQueryService(db).GetRetryCandidatesAsync(
            NotificationJournalFilter.All with { Statuses = [NotificationStatus.Sent] }, limit: 10);

        Assert.That(items.Select(i => i.Id), Is.EqualTo(new[] { failed }));
    }

    /// <summary>
    /// An order's item list has no fixed size. A bounded column would truncate — or refuse — a large one at insert time,
    /// and a truncated payload fails to deserialize at the one moment someone needs it.
    /// </summary>
    [Test]
    public async Task APayloadLargerThanAnyVarcharBound_IsKeptWhole()
    {
        var order = new OrderCreatedEvent
        {
            EventId = Guid.NewGuid(),
            UserId = "user-1",
            Items = Enumerable.Range(0, 400)
                .Select(i => new OrderEventItem { ProductId = Guid.NewGuid(), ProductName = $"Product {i}", Quantity = 1 })
                .ToList()
        };
        var payload = NotificationPayload.Serialize(order)!;
        Assert.That(payload.Length, Is.GreaterThan(40_000), "precondition: far past any varchar bound the table uses");

        var id = await SeedAsync(
            NotificationLog.CreatePending(order.EventId, nameof(OrderCreatedEvent), null, "user-1", "order-created", "s", payload),
            Day);

        await using var db = NewContext();
        var stored = await db.NotificationLogs.AsNoTracking().SingleAsync(l => l.Id == id);
        var back = (OrderCreatedEvent)NotificationPayload.Deserialize(stored.Payload!, typeof(OrderCreatedEvent));
        Assert.That(back.Items, Has.Count.EqualTo(400));
    }

    /// <summary>
    /// The race S13's design leans on. The operator reads a Failed row; before the operator saves, a redelivery claims
    /// it, sends the email and records Sent. The operator's save must then fail its row-version check — otherwise it
    /// would overwrite Sent with Undeliverable, and the journal would say a customer never got an email they have. The
    /// endpoint maps the exception to 409 (<c>NotificationOperatorWriteConflictTests</c>).
    /// </summary>
    [Test]
    public async Task AnOperatorsMarkThatLosesToADelivery_FailsItsConcurrencyCheck_AndTheDeliveryStands()
    {
        var refund = new PaymentRefundedEvent { EventId = Guid.NewGuid(), OrderId = Guid.NewGuid(), UserId = "user-1", Amount = 5m };
        var failed = NotificationLog.CreatePending(
            refund.EventId, nameof(PaymentRefundedEvent), null, "user-1", "payment-refunded", "s", NotificationPayload.Serialize(refund));
        failed.BeginAttempt(DateTime.UtcNow);
        failed.MarkFailed("Simulated SMTP failure");
        var id = await SeedAsync(failed, Day);

        var deliveryRan = false;
        await using var operatorDb = NewContext();
        var handler = new MarkNotificationUndeliverableCommandHandler(
            new DeliveryAfterRead(new NotificationLogRepository(operatorDb), async () =>
            {
                await DeliverAsync(refund);
                deliveryRan = true;
            }),
            Mock.Of<ICurrentUserContext>(),
            TimeProvider.System,
            NullLogger<MarkNotificationUndeliverableCommandHandler>.Instance);

        Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            handler.Handle(new MarkNotificationUndeliverableCommand(id, "closing it"), CancellationToken.None));

        await using var check = NewContext();
        var stored = await check.NotificationLogs.AsNoTracking().SingleAsync(l => l.Id == id);
        Assert.Multiple(() =>
        {
            Assert.That(deliveryRan, Is.True, "precondition: the delivery ran between the operator's read and save");
            Assert.That(stored.Status, Is.EqualTo(NotificationStatus.Sent), "the delivery's outcome stands");
            Assert.That(stored.LastError, Is.Null);
        });
    }

    // ---------- helpers ----------

    private NotificationDbContext NewContext() => new(new DbContextOptionsBuilder<NotificationDbContext>()
        .UseNpgsql(_connectionString)
        .Options);

    /// <summary>Inserts, then dates the row with ExecuteUpdate: BaseDbContext overwrites CreatedAt on insert.</summary>
    private async Task<Guid> SeedAsync(NotificationLog log, DateTime createdAt)
    {
        await using var db = NewContext();
        db.NotificationLogs.Add(log);
        await db.SaveChangesAsync();
        await db.NotificationLogs.Where(l => l.Id == log.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.CreatedAt, createdAt));
        return log.Id;
    }

    private static NotificationLog Pending(bool payload = true)
    {
        var refund = new PaymentRefundedEvent { EventId = Guid.NewGuid(), OrderId = Guid.NewGuid(), UserId = "user-1", Amount = 1m };
        return NotificationLog.CreatePending(
            refund.EventId, nameof(PaymentRefundedEvent), null, "user-1", "payment-refunded", "s",
            payload ? NotificationPayload.Serialize(refund) : null);
    }

    private static NotificationLog Failed(bool payload)
    {
        var log = Pending(payload);
        log.BeginAttempt(DateTime.UtcNow);
        log.MarkFailed("Simulated SMTP failure");
        return log;
    }

    private static NotificationLog Sent()
    {
        var log = Pending();
        log.BeginAttempt(DateTime.UtcNow);
        log.MarkSent("<m@test>");
        return log;
    }

    /// <summary>The real consumer, on its own connection, as a redelivery would run.</summary>
    private async Task DeliverAsync(PaymentRefundedEvent refund)
    {
        await using var db = NewContext();
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendPaymentRefundedAsync(
                It.IsAny<RecipientAddress>(), It.IsAny<PaymentRefundedEmailModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("<refund@test>");
        var resolver = new Mock<IUserContactResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecipientLookup.Found(new RecipientAddress("user@test.com")));

        var consumer = new PaymentRefundedConsumer(
            new NotificationLogRepository(db),
            email.Object,
            resolver.Object,
            Options.Create(new SmtpSettings { FromEmail = "support@eshop.local" }),
            TimeProvider.System,
            NullLogger<PaymentRefundedConsumer>.Instance);

        var context = new Mock<ConsumeContext<PaymentRefundedEvent>>();
        context.SetupGet(x => x.Message).Returns(refund);
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);
        await consumer.Consume(context.Object);
    }

    /// <summary>Runs <c>between</c> once, after the operator's read and before anything is written.</summary>
    private sealed class DeliveryAfterRead(INotificationLogRepository inner, Func<Task> between) : INotificationLogRepository
    {
        public async Task<NotificationLog?> FindByIdAsync(Guid id, CancellationToken ct = default)
        {
            var log = await inner.FindByIdAsync(id, ct);
            await between();
            return log;
        }

        public Task<NotificationLog?> FindByEventIdAsync(Guid eventId, CancellationToken ct = default) => inner.FindByEventIdAsync(eventId, ct);

        public Task<bool> TryAddAsync(NotificationLog log, CancellationToken ct = default) => inner.TryAddAsync(log, ct);

        public Task SaveAsync(NotificationLog log, CancellationToken ct = default) => inner.SaveAsync(log, ct);
    }
}
