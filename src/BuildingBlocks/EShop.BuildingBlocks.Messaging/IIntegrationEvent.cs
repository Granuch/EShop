namespace EShop.BuildingBlocks.Messaging;

/// <summary>
/// Base interface for integration events (cross-service communication via MassTransit).
/// Deliberately does NOT extend MediatR.INotification to prevent accidental
/// in-process handling — integration events are published only via the outbox + MassTransit.
/// </summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTime OccurredOn { get; }
}
