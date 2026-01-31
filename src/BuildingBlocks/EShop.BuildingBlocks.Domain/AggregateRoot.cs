namespace EShop.BuildingBlocks.Domain;

/// <summary>
/// Marker interface for aggregate roots (used for domain event detection in DbContext)
/// </summary>
public interface IAggregateRootMarker { }

/// <summary>
/// Base class for aggregate roots in DDD
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRootMarker
{
    private readonly List<IDomainEvent> _domainEvents = new();

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }

    /// <summary>
    /// Version for optimistic concurrency control
    /// </summary>
    public int Version { get; protected set; }
}
