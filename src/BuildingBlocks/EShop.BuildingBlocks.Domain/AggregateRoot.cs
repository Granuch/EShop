namespace EShop.BuildingBlocks.Domain;

/// <summary>
/// Marker interface for aggregate roots (used for domain event detection in DbContext)
/// </summary>
public interface IAggregateRootMarker { }

/// <summary>
/// Base class for aggregate roots in DDD.
/// 
/// Features:
/// - Domain event collection and management
/// - Optimistic concurrency control via Version property
/// - Audit fields inherited from Entity
/// 
/// Usage:
/// - Call AddDomainEvent() when state changes that other parts of the system need to know about
/// - Version is automatically incremented and checked by EF Core for concurrent modifications
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
    /// Version for optimistic concurrency control.
    /// This property is configured as a concurrency token in EF Core.
    /// It is automatically incremented on each SaveChanges.
    /// If another process modified the aggregate, EF Core will throw DbUpdateConcurrencyException.
    /// </summary>
    public int Version { get; protected set; }

    /// <summary>
    /// Increments the version number. Called automatically by the DbContext
    /// when saving changes. Can also be called explicitly in domain methods
    /// that represent significant state transitions.
    /// </summary>
    public void IncrementVersion()
    {
        Version++;
    }
}
