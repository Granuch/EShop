namespace EShop.BuildingBlocks.Messaging;

/// <summary>
/// Base class for integration events (cross-service communication).
/// Immutable record with correlation support and schema versioning.
/// </summary>
public abstract record IntegrationEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Correlation ID for distributed tracing across services.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Schema version for backward-compatible event evolution.
    /// </summary>
    public int Version { get; init; } = 1;
}
