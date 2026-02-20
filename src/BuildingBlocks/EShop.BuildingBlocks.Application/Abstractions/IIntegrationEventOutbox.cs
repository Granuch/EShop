using EShop.BuildingBlocks.Messaging;

namespace EShop.BuildingBlocks.Application.Abstractions;

/// <summary>
/// Abstraction for publishing integration events via the outbox pattern.
/// The Application layer uses this interface to enqueue integration events;
/// the Infrastructure layer persists them in the outbox table for reliable delivery.
/// 
/// This keeps the Application layer unaware of MassTransit or any messaging transport.
/// </summary>
public interface IIntegrationEventOutbox
{
    /// <summary>
    /// Enqueues an integration event for later publishing.
    /// The event is persisted in the outbox table within the current transaction.
    /// </summary>
    void Enqueue(IIntegrationEvent integrationEvent, string? correlationId = null);
}
