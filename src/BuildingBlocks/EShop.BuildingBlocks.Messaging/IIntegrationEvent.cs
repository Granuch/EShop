using MediatR;

namespace EShop.BuildingBlocks.Messaging;

/// <summary>
/// Base interface for integration events (cross-service communication)
/// </summary>
public interface IIntegrationEvent : INotification
{
    Guid EventId { get; }
    DateTime OccurredOn { get; }
}
