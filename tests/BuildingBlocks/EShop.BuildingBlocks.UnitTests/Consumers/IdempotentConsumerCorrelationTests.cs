using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Infrastructure.Consumers;
using EShop.BuildingBlocks.Infrastructure.Services;
using EShop.BuildingBlocks.Messaging;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EShop.BuildingBlocks.UnitTests.Consumers;

/// <summary>
/// M7, consumer half (Catalog audit Stage 7). A consumer's scope has no <c>HttpContext</c>, so
/// anything it wrote — its own domain events, and therefore its own outbox rows — used to be stamped
/// with a freshly minted correlation id, breaking the trace on the receiving side of every hop.
/// Driven through the real <see cref="IdempotentConsumer{TMessage,TDbContext}.Consume"/> (on its
/// in-memory claim path) so the assertion covers the wiring, not just a helper.
/// </summary>
[TestFixture]
public class IdempotentConsumerCorrelationTests
{
    [Test]
    public async Task TheHandlerSeesThePayloadsCorrelationId()
    {
        var (consumer, context) = Arrange(payloadCorrelationId: "req-abc", transportCorrelationId: Guid.NewGuid());

        await consumer.Consume(context);

        Assert.That(consumer.SeenCorrelationId, Is.EqualTo("req-abc"),
            "the payload carries the originating id verbatim; the transport header is a reformatted or substituted GUID");
    }

    [Test]
    public async Task WithoutAPayloadId_TheTransportCorrelationIdIsUsed()
    {
        var transport = Guid.NewGuid();
        var (consumer, context) = Arrange(payloadCorrelationId: null, transportCorrelationId: transport);

        await consumer.Consume(context);

        Assert.That(consumer.SeenCorrelationId, Is.EqualTo(transport.ToString()));
    }

    [Test]
    public async Task TheAmbientIdEndsWithTheMessage()
    {
        var (consumer, context) = Arrange(payloadCorrelationId: "req-def", transportCorrelationId: null);

        await consumer.Consume(context);

        Assert.That(AmbientCorrelation.Current, Is.Null);
    }

    private static (CapturingConsumer Consumer, ConsumeContext<TestEvent> Context) Arrange(
        string? payloadCorrelationId, Guid? transportCorrelationId)
    {
        var db = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase($"consumer-correlation-{Guid.NewGuid():N}")
            .Options);

        var context = new Mock<ConsumeContext<TestEvent>>();
        context.SetupGet(c => c.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(c => c.CorrelationId).Returns(transportCorrelationId);
        context.SetupGet(c => c.Message).Returns(new TestEvent { CorrelationId = payloadCorrelationId });
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        return (new CapturingConsumer(db), context.Object);
    }

    public sealed record TestEvent : IntegrationEvent;

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<ProcessedMessage>().HasKey(m => m.MessageId);
    }

    /// <summary>
    /// Reads the id the way production code does — through <see cref="HttpCurrentUserContext"/> in a
    /// scope with no HttpContext — rather than peeking at <see cref="AmbientCorrelation"/> directly.
    /// </summary>
    private sealed class CapturingConsumer(TestDbContext db)
        : IdempotentConsumer<TestEvent, TestDbContext>(db, NullLogger.Instance)
    {
        public string? SeenCorrelationId { get; private set; }

        protected override Task HandleAsync(ConsumeContext<TestEvent> context, CancellationToken cancellationToken)
        {
            SeenCorrelationId = new HttpCurrentUserContext(new HttpContextAccessor()).CorrelationId;
            return Task.CompletedTask;
        }
    }
}
