namespace EShop.BuildingBlocks.Domain;

/// <summary>
/// Interface for dispatching domain events
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>
    /// Dispatches all domain events from the given aggregate roots
    /// </summary>
    Task DispatchEventsAsync(IEnumerable<AggregateRoot<object>> aggregateRoots, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches a single domain event
    /// </summary>
    Task DispatchEventAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}
