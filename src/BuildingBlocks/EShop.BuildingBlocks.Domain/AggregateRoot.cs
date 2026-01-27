namespace EShop.BuildingBlocks.Domain;

/// <summary>
/// Base class for aggregate roots in DDD
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>
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

    // TODO: Implement versioning for optimistic concurrency control
    // public int Version { get; protected set; }
}
