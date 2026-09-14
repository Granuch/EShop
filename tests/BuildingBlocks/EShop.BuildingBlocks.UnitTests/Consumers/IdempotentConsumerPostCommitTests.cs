using EShop.BuildingBlocks.Infrastructure.Consumers;
using EShop.BuildingBlocks.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EShop.BuildingBlocks.UnitTests.Consumers;

/// <summary>
/// <see cref="IdempotentConsumer{TMessage,TDbContext}"/>'s post-commit hook (Ordering audit H4). Cache
/// invalidation moved there because a consumer owns its transaction: done inside it, a concurrent read
/// re-caches the pre-commit state. Driven through the real <c>Consume</c> on its in-memory claim path,
/// where "committed" means the handler returned; the relational path calls the hook right after
/// <c>CommitAsync</c>.
/// </summary>
[TestFixture]
public class IdempotentConsumerPostCommitTests
{
    [Test]
    public async Task TheHook_RunsAfterTheHandler()
    {
        var consumer = new RecordingConsumer(NewDb());

        await consumer.Consume(ContextFor(Guid.NewGuid()));

        Assert.That(consumer.Calls, Is.EqualTo(new[] { "handle", "committed" }));
    }

    [Test]
    public void TheHook_DoesNotRun_WhenTheHandlerThrows()
    {
        var consumer = new RecordingConsumer(NewDb()) { HandleThrows = true };

        Assert.ThrowsAsync<InvalidOperationException>(() => consumer.Consume(ContextFor(Guid.NewGuid())));

        Assert.That(consumer.Calls, Is.EqualTo(new[] { "handle" }));
    }

    // No duplicate-message case here, deliberately. The in-memory claim path cannot detect a duplicate
    // made from a second DbContext: EF InMemory reports the key clash as ArgumentException, while
    // TryClaimMessageInMemoryAsync catches only DbUpdateException, so the redelivery throws instead of
    // being skipped. That is a pre-existing gap in the test-only path (production uses ON CONFLICT on
    // Postgres), left for its own change. Both paths return before RunOnCommittedAsync on a duplicate.

    /// <summary>The writes are already durable; a cache outage must not turn them into a redelivery.</summary>
    [Test]
    public async Task AFailingHook_DoesNotFailTheMessage()
    {
        var db = NewDb();
        var consumer = new RecordingConsumer(db) { HookThrows = true };

        await consumer.Consume(ContextFor(Guid.NewGuid()));

        Assert.That(consumer.Calls, Is.EqualTo(new[] { "handle", "committed" }));
        Assert.That(db.Set<ProcessedMessage>().Count(), Is.EqualTo(1), "the claim stands, so a redelivery is skipped");
    }

    private static TestDbContext NewDb() => new(new DbContextOptionsBuilder<TestDbContext>()
        .UseInMemoryDatabase($"consumer-post-commit-{Guid.NewGuid():N}")
        .Options);

    private static ConsumeContext<TestEvent> ContextFor(Guid messageId)
    {
        var context = new Mock<ConsumeContext<TestEvent>>();
        context.SetupGet(c => c.MessageId).Returns(messageId);
        context.SetupGet(c => c.Message).Returns(new TestEvent());
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    public sealed record TestEvent : IntegrationEvent;

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<ProcessedMessage>().HasKey(m => m.MessageId);
    }

    private sealed class RecordingConsumer(TestDbContext db)
        : IdempotentConsumer<TestEvent, TestDbContext>(db, NullLogger.Instance)
    {
        public List<string> Calls { get; } = [];
        public bool HandleThrows { get; init; }
        public bool HookThrows { get; init; }

        protected override Task HandleAsync(ConsumeContext<TestEvent> context, CancellationToken cancellationToken)
        {
            Calls.Add("handle");
            return HandleThrows ? throw new InvalidOperationException("handler failed") : Task.CompletedTask;
        }

        protected override Task OnCommittedAsync(ConsumeContext<TestEvent> context, CancellationToken cancellationToken)
        {
            Calls.Add("committed");
            return HookThrows ? throw new InvalidOperationException("cache down") : Task.CompletedTask;
        }
    }
}
