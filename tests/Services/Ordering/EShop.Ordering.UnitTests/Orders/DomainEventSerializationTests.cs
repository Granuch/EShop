using System.Reflection;
using System.Text.Json;
using EShop.BuildingBlocks.Domain;
using EShop.Ordering.Domain.Events;

namespace EShop.Ordering.UnitTests.Orders;

/// <summary>
/// Audit L3. With the outbox on (the default), a domain event never reaches its handler as the object
/// the aggregate raised: <c>BaseDbContext</c> serializes it into <c>outbox_messages</c> and
/// <c>OutboxProcessorService</c> deserializes a new one. Any property System.Text.Json cannot set is
/// silently rebuilt from its initializer on the way back — which is how every Ordering event's
/// <c>OccurredOn</c> became the processor's clock, and so <c>OrderShippedEvent.ShippedAt</c> too.
/// </summary>
[TestFixture]
public class DomainEventSerializationTests
{
    // The options on each side of the trip: BaseDbContext.JsonOptions writes,
    // OutboxProcessorService.JsonOptions reads. Both are private, so they are restated here.
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static IEnumerable<Type> OrderingDomainEvents() =>
        typeof(OrderCreatedDomainEvent).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IDomainEvent).IsAssignableFrom(t));

    /// <summary>
    /// Structural, so an event added later is covered too: every public property must have a public
    /// setter (init counts), or the deserializer cannot restore it.
    /// </summary>
    [TestCaseSource(nameof(OrderingDomainEvents))]
    public void EveryPropertyOfADomainEvent_CanBeRestoredByTheDeserializer(Type eventType)
    {
        var unsettable = eventType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is not { IsPublic: true })
            .Select(p => p.Name)
            .ToList();

        Assert.That(unsettable, Is.Empty, $"{eventType.Name} has properties the outbox round trip would reset");
    }

    [Test]
    public void TheDomainEventsAreDiscovered()
    {
        Assert.That(OrderingDomainEvents().Count(), Is.GreaterThanOrEqualTo(4));
    }

    [Test]
    public void AShippedEvent_KeepsItsTime_AcrossTheOutbox()
    {
        var raised = new OrderShippedDomainEvent
        {
            OccurredOn = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
            OrderId = Guid.NewGuid(),
            UserId = "user-1"
        };

        var restored = RoundTrip(raised);

        Assert.That(restored.OccurredOn, Is.EqualTo(raised.OccurredOn));
        Assert.That(restored.EventId, Is.EqualTo(raised.EventId));
    }

    [Test]
    public void ACreatedEvent_KeepsItsLines_AcrossTheOutbox()
    {
        var raised = new OrderCreatedDomainEvent
        {
            OrderId = Guid.NewGuid(),
            UserId = "user-1",
            TotalAmount = 21.00m,
            Items = [new OrderCreatedLine { ProductId = Guid.NewGuid(), ProductName = "Widget", UnitPrice = 10.50m, Quantity = 2 }]
        };

        var restored = RoundTrip(raised);

        Assert.That(restored.Items, Is.EqualTo(raised.Items));
        Assert.That(restored.OccurredOn, Is.EqualTo(raised.OccurredOn));
    }

    private static T RoundTrip<T>(T domainEvent) where T : IDomainEvent =>
        (T)JsonSerializer.Deserialize(
            JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), WriteOptions),
            domainEvent.GetType(),
            ReadOptions)!;
}
