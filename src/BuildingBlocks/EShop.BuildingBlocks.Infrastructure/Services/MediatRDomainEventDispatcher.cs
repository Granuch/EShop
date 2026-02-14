using EShop.BuildingBlocks.Domain;
using MediatR;

namespace EShop.BuildingBlocks.Infrastructure.Services;

/// <summary>
/// Domain event dispatcher using MediatR
/// </summary>
public class MediatRDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IMediator _mediator;

    public MediatRDomainEventDispatcher(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task DispatchEventsAsync(IEnumerable<IAggregateRootMarker> aggregateRoots, CancellationToken cancellationToken = default)
    {
        var domainEvents = new List<IDomainEvent>();

        foreach (var entity in aggregateRoots)
        {
            var eventsProperty = entity.GetType().GetProperty("DomainEvents");
            if (eventsProperty?.GetValue(entity) is IReadOnlyList<IDomainEvent> events)
            {
                domainEvents.AddRange(events);
            }
        }

        foreach (var entity in aggregateRoots)
        {
            var clearMethod = entity.GetType().GetMethod("ClearDomainEvents");
            clearMethod?.Invoke(entity, null);
        }

        foreach (var domainEvent in domainEvents)
        {
            await DispatchEventAsync(domainEvent, cancellationToken);
        }
    }

    public async Task DispatchEventAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        await _mediator.Publish(domainEvent, cancellationToken);
    }
}
