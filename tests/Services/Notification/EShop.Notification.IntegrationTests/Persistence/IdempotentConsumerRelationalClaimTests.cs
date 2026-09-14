using EShop.BuildingBlocks.Infrastructure.Consumers;
using EShop.BuildingBlocks.Messaging;
using EShop.Notification.Infrastructure.Data;
using EShop.Notification.IntegrationTests.Fixtures;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EShop.Notification.IntegrationTests.Persistence;

/// <summary>
/// BuildingBlocks' <c>IdempotentConsumer</c>, relational path, on a real <c>processed_messages</c> table. Notification's
/// own consumers stopped using it in Notification audit S2 (D5), but Ordering's and Payment's still do, and S1's
/// Notification test was the only guard of its duplicate skip. A probe consumer keeps that guard here, where a migrated
/// Postgres database with the claim table already exists. Its rollback is guarded by Payment's
/// <c>PaymentWriteCollisionTests</c>.
/// </summary>
[TestFixture]
public class IdempotentConsumerRelationalClaimTests
{
    private string _connectionString = null!;

    [SetUp]
    public async Task CreateDatabaseAsync() => _connectionString = await PostgresTestServer.CreateDatabaseAsync();

    [TearDown]
    public void ReleaseDatabase() => PostgresTestServer.ReleaseDatabase(_connectionString);

    /// <summary>The second payload differs, so nothing but the MessageId claim can stop it.</summary>
    [Test]
    public async Task TheSameMessageIdDeliveredTwice_IsHandledOnce_AndTheDuplicateIsAcknowledged()
    {
        var handled = new HandledCounter();
        var messageId = Guid.NewGuid();

        await ConsumeAsync(new ProbeEvent(), messageId, handled);
        Assert.DoesNotThrowAsync(() => ConsumeAsync(new ProbeEvent(), messageId, handled));

        await using var db = NewContext();
        var claims = await db.Set<ProcessedMessage>().CountAsync(m => m.MessageId == messageId);
        Assert.Multiple(() =>
        {
            Assert.That(handled.Count, Is.EqualTo(1));
            Assert.That(claims, Is.EqualTo(1));
        });
    }

    private NotificationDbContext NewContext() => new(new DbContextOptionsBuilder<NotificationDbContext>()
        .UseNpgsql(_connectionString)
        .Options);

    private async Task ConsumeAsync(ProbeEvent message, Guid messageId, HandledCounter handled)
    {
        await using var db = NewContext();
        var context = new Mock<ConsumeContext<ProbeEvent>>();
        context.SetupGet(x => x.Message).Returns(message);
        context.SetupGet(x => x.MessageId).Returns(messageId);
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        await new ProbeConsumer(db, handled).Consume(context.Object);
    }

    public sealed record ProbeEvent : IntegrationEvent;

    private sealed class HandledCounter
    {
        public int Count { get; set; }
    }

    private sealed class ProbeConsumer(NotificationDbContext db, HandledCounter handled)
        : IdempotentConsumer<ProbeEvent, NotificationDbContext>(db, NullLogger.Instance)
    {
        protected override Task HandleAsync(ConsumeContext<ProbeEvent> context, CancellationToken cancellationToken)
        {
            handled.Count++;
            return Task.CompletedTask;
        }
    }
}
